using Microsoft.Extensions.Logging.Abstractions;
using Payments.Application.Processing;
using Payments.Domain;

namespace Payments.Application.Tests.Processing;

public class ProcessStripeEventPropertyTests
{
    private const string PaymentIntentId = "pi_property";
    private const long Amount = 2000;
    private const string Currency = "eur";
    private const int RepeatedDeliveryRuns = 1000;
    private const int RepeatedDeliverySeed = 27;
    private const int MaxCopiesPerEvent = 3;

    private static readonly DateTimeOffset processedAt = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, Lifecycle> lifecycles = new()
    {
        ["authenticated, processed, then refunded in three steps"] = new(
            [
                PaymentIntentEvent("evt_created", "payment_intent.created"),
                PaymentIntentEvent("evt_requires_action", "payment_intent.requires_action"),
                PaymentIntentEvent("evt_processing", "payment_intent.processing"),
                PaymentIntentEvent("evt_succeeded", "payment_intent.succeeded"),
                RefundEvent("evt_refund_500", 500),
                RefundEvent("evt_refund_1200", 1200),
                RefundEvent("evt_refund_full", Amount),
            ],
            PaymentStatus.Refunded,
            Amount,
            "evt_refund_full"),
        ["declined twice, paid on retry, then partially refunded twice"] = new(
            [
                PaymentIntentEvent("evt_created", "payment_intent.created"),
                PaymentIntentEvent("evt_payment_failed_1", "payment_intent.payment_failed"),
                PaymentIntentEvent("evt_requires_action", "payment_intent.requires_action"),
                PaymentIntentEvent("evt_payment_failed_2", "payment_intent.payment_failed"),
                PaymentIntentEvent("evt_succeeded", "payment_intent.succeeded"),
                RefundEvent("evt_refund_500", 500),
                RefundEvent("evt_refund_1200", 1200),
            ],
            PaymentStatus.PartiallyRefunded,
            1200,
            "evt_refund_1200"),
        ["authentication failed, then canceled"] = new(
            [
                PaymentIntentEvent("evt_created", "payment_intent.created"),
                PaymentIntentEvent("evt_requires_action", "payment_intent.requires_action"),
                PaymentIntentEvent("evt_payment_failed", "payment_intent.payment_failed"),
                PaymentIntentEvent("evt_canceled", "payment_intent.canceled"),
            ],
            PaymentStatus.Canceled,
            0,
            "evt_canceled"),
        ["authentication failed, no retry"] = new(
            [
                PaymentIntentEvent("evt_created", "payment_intent.created"),
                PaymentIntentEvent("evt_requires_action", "payment_intent.requires_action"),
                PaymentIntentEvent("evt_payment_failed", "payment_intent.payment_failed"),
            ],
            PaymentStatus.Failed,
            0,
            "evt_payment_failed"),
        ["processed asynchronously, then paid"] = new(
            [
                PaymentIntentEvent("evt_created", "payment_intent.created"),
                PaymentIntentEvent("evt_processing", "payment_intent.processing"),
                PaymentIntentEvent("evt_succeeded", "payment_intent.succeeded"),
            ],
            PaymentStatus.Paid,
            0,
            "evt_succeeded"),
    };

    public static TheoryData<string> LifecycleNames => new(lifecycles.Keys);

    [Theory]
    [MemberData(nameof(LifecycleNames))]
    public async Task Every_delivery_order_ends_in_the_same_state(string lifecycleName)
    {
        Lifecycle lifecycle = lifecycles[lifecycleName];
        int orders = 0;

        foreach (StripeEvent[] order in Permutations(lifecycle.Events))
        {
            await AssertEndsInExpectedState(order, lifecycle.Expected);
            orders++;
        }

        Assert.Equal(Factorial(lifecycle.Events.Length), orders);
    }

    [Theory]
    [MemberData(nameof(LifecycleNames))]
    public async Task Repeated_deliveries_in_any_order_end_in_the_same_state(string lifecycleName)
    {
        Lifecycle lifecycle = lifecycles[lifecycleName];
        Random random = new(RepeatedDeliverySeed);

        for (int run = 0; run < RepeatedDeliveryRuns; run++)
        {
            StripeEvent[] deliveries =
            [
                .. lifecycle.Events.SelectMany(stripeEvent => Enumerable.Repeat(stripeEvent, random.Next(1, MaxCopiesPerEvent + 1))),
            ];
            random.Shuffle(deliveries);

            await AssertEndsInExpectedState(deliveries, lifecycle.Expected);
        }
    }

    [Fact]
    public async Task Requires_action_and_processing_as_the_highest_events_end_in_the_last_to_arrive()
    {
        StripeEvent created = PaymentIntentEvent("evt_created", "payment_intent.created");
        StripeEvent requiresAction = PaymentIntentEvent("evt_requires_action", "payment_intent.requires_action");
        StripeEvent processing = PaymentIntentEvent("evt_processing", "payment_intent.processing");

        ResultingState requiresActionLast = await Deliver([created, processing, requiresAction]);
        ResultingState processingLast = await Deliver([created, requiresAction, processing]);

        Assert.Equal(PaymentStatus.RequiresAction, requiresActionLast.Status);
        Assert.Equal(PaymentStatus.Processing, processingLast.Status);
    }

    private static async Task AssertEndsInExpectedState(StripeEvent[] deliveries, ResultingState expected)
    {
        ResultingState actual = await Deliver(deliveries);
        if (actual != expected)
        {
            string order = string.Join(", ", deliveries.Select(stripeEvent => stripeEvent.Id));
            Assert.Fail($"Delivering {order} ended in {actual}, expected {expected}.");
        }
    }

    private static async Task<ResultingState> Deliver(StripeEvent[] deliveries)
    {
        InMemoryPaymentStore store = new();
        ProcessStripeEvent processStripeEvent = new(store, new TestTransientErrorDetector(), new TestFaultInjector(), new DelayTrackingTimeProvider(processedAt), NullLogger<ProcessStripeEvent>.Instance);

        foreach (StripeEvent delivery in deliveries)
        {
            await processStripeEvent.ExecuteAsync(delivery.Payload, CancellationToken.None);
        }

        Payment payment = Assert.Single(store.Payments.Values);
        return new ResultingState(
            payment.Status,
            payment.Amount,
            payment.AmountRefunded,
            payment.Currency,
            payment.LastEventId,
            store.InboxEvents.Count,
            store.History.Count);
    }

    private static IEnumerable<StripeEvent[]> Permutations(StripeEvent[] events)
    {
        if (events.Length <= 1)
        {
            yield return events;
            yield break;
        }

        for (int first = 0; first < events.Length; first++)
        {
            StripeEvent[] rest = [.. events[..first], .. events[(first + 1)..]];
            foreach (StripeEvent[] tail in Permutations(rest))
            {
                yield return [events[first], .. tail];
            }
        }
    }

    private static int Factorial(int n)
    {
        return n <= 1 ? 1 : n * Factorial(n - 1);
    }

    private static StripeEvent PaymentIntentEvent(string eventId, string type)
    {
        return new StripeEvent(eventId, $$"""
            {"id":"{{eventId}}","type":"{{type}}","created":1792181099,
             "data":{"object":{"id":"{{PaymentIntentId}}","object":"payment_intent","amount":{{Amount}},"currency":"{{Currency}}"} } }
            """);
    }

    private static StripeEvent RefundEvent(string eventId, long amountRefunded)
    {
        return new StripeEvent(eventId, $$"""
            {"id":"{{eventId}}","type":"charge.refunded","created":1792181099,
             "data":{"object":{"id":"ch_property","object":"charge","payment_intent":"{{PaymentIntentId}}","amount":{{Amount}},"amount_refunded":{{amountRefunded}},"currency":"{{Currency}}"} } }
            """);
    }

    private sealed record StripeEvent(string Id, string Payload);

    private sealed record ResultingState(
        PaymentStatus Status,
        long Amount,
        long AmountRefunded,
        string Currency,
        string LastEventId,
        int InboxRows,
        int HistoryRows);

    private sealed record Lifecycle(StripeEvent[] Events, PaymentStatus Status, long AmountRefunded, string LastEventId)
    {
        public ResultingState Expected => new(this.Status, Amount, this.AmountRefunded, Currency, this.LastEventId, this.Events.Length, this.Events.Length);
    }
}
