using Microsoft.Extensions.Logging;
using Payments.Application.Events;
using Payments.Application.Processing;
using Payments.Application.Tests.Webhooks;
using Payments.Domain;

namespace Payments.Application.Tests.Processing;

public class ProcessStripeEventTests
{
    private static readonly string fixturesDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly DateTimeOffset earlierAt = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset processedAt = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryPaymentStore store = new();
    private readonly CollectingLogger<ProcessStripeEvent> logger = new();

    [Fact]
    public async Task Event_for_an_unknown_payment_creates_it_in_the_requested_state()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);

        ProcessStripeEventResult result = await this.Process(payload);

        Assert.Equal(new ProcessStripeEventResult(ProcessStripeEventOutcome.Applied), result);
        Payment payment = Assert.Single(this.store.Payments.Values);
        Assert.Equal(paymentEvent.PaymentIntentId, payment.PaymentIntentId);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(paymentEvent.Amount, payment.Amount);
        Assert.Equal(0, payment.AmountRefunded);
        Assert.Equal(paymentEvent.Currency, payment.Currency);
        Assert.Equal(paymentEvent.EventId, payment.LastEventId);
        Assert.Equal(processedAt, payment.CreatedAt);
        Assert.Equal(processedAt, payment.UpdatedAt);

        InboxEvent inboxEvent = Assert.Single(this.store.InboxEvents);
        Assert.Equal(paymentEvent.EventId, inboxEvent.EventId);
        Assert.Equal(paymentEvent.EventType, inboxEvent.EventType);
        Assert.Equal(paymentEvent.PaymentIntentId, inboxEvent.PaymentIntentId);
        Assert.Equal(paymentEvent.StripeCreatedAt, inboxEvent.StripeCreatedAt);
        Assert.Equal(processedAt, inboxEvent.ReceivedAt);
        Assert.Equal(TransitionOutcome.Applied, inboxEvent.Outcome);
        Assert.Null(inboxEvent.TraceId);
        Assert.Equal(payload, inboxEvent.Payload);

        PaymentHistoryEntry entry = Assert.Single(this.store.History);
        Assert.Null(entry.FromStatus);
        Assert.Equal(PaymentStatus.Paid, entry.ToStatus);
        Assert.True(entry.Applied);
        Assert.Null(entry.Reason);
        Assert.Equal(1, this.store.Commits);
    }

    [Fact]
    public async Task Duplicate_event_writes_nothing_and_returns_duplicate()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);
        await this.Process(payload);

        ProcessStripeEventResult result = await this.Process(payload);

        Assert.Equal(new ProcessStripeEventResult(ProcessStripeEventOutcome.Duplicate), result);
        Assert.Single(this.store.InboxEvents);
        Assert.Single(this.store.History);
        Assert.Equal(PaymentStatus.Paid, this.store.Payments[paymentEvent.PaymentIntentId].Status);
        Assert.Equal(1, this.store.Commits);
        Assert.Contains(this.logger.Entries, entry => entry.Message.Contains(paymentEvent.EventId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Partial_refund_of_a_paid_payment_records_the_refunded_amount()
    {
        string payload = ReadFixture("charge.refunded.partial.json");
        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);
        this.Seed(paymentEvent, PaymentStatus.Paid, amountRefunded: 0);

        ProcessStripeEventResult result = await this.Process(payload);

        Assert.Equal(new ProcessStripeEventResult(ProcessStripeEventOutcome.Applied), result);
        Payment payment = this.store.Payments[paymentEvent.PaymentIntentId];
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(paymentEvent.AmountRefunded, payment.AmountRefunded);
        Assert.True(paymentEvent.AmountRefunded < payment.Amount);
        Assert.Equal(paymentEvent.EventId, payment.LastEventId);
        PaymentHistoryEntry entry = Assert.Single(this.store.History);
        Assert.Equal(PaymentStatus.Paid, entry.FromStatus);
        Assert.Equal(PaymentStatus.PartiallyRefunded, entry.ToStatus);
        Assert.True(entry.Applied);
    }

    [Fact]
    public async Task Larger_partial_refund_raises_the_refunded_amount_and_keeps_the_state()
    {
        string payload = ReadFixture("charge.refunded.partial.json");
        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);
        this.Seed(paymentEvent, PaymentStatus.PartiallyRefunded, amountRefunded: paymentEvent.AmountRefunded - 1);

        ProcessStripeEventResult result = await this.Process(payload);

        Assert.Equal(ProcessStripeEventOutcome.Applied, result.Outcome);
        Payment payment = this.store.Payments[paymentEvent.PaymentIntentId];
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(paymentEvent.AmountRefunded, payment.AmountRefunded);
    }

    [Fact]
    public async Task Partial_refund_with_the_same_amount_is_recorded_as_redundant()
    {
        string payload = ReadFixture("charge.refunded.partial.json");
        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);
        this.Seed(paymentEvent, PaymentStatus.PartiallyRefunded, amountRefunded: paymentEvent.AmountRefunded);

        ProcessStripeEventResult result = await this.Process(payload);

        Assert.Equal(ProcessStripeEventOutcome.Redundant, result.Outcome);
        Assert.Equal(TransitionOutcome.Redundant, Assert.Single(this.store.InboxEvents).Outcome);
        PaymentHistoryEntry entry = Assert.Single(this.store.History);
        Assert.False(entry.Applied);
        Assert.Equal(ProcessStripeEvent.RedundantReason, entry.Reason);
        Assert.Equal("evt_seeded", this.store.Payments[paymentEvent.PaymentIntentId].LastEventId);
    }

    [Fact]
    public async Task Event_that_would_lower_the_level_is_recorded_as_out_of_order()
    {
        string payload = ReadFixture("payment_intent.created.json");
        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);
        this.Seed(paymentEvent, PaymentStatus.Paid, amountRefunded: 0);

        ProcessStripeEventResult result = await this.Process(payload);

        Assert.Equal(new ProcessStripeEventResult(ProcessStripeEventOutcome.IgnoredOutOfOrder), result);
        Assert.Equal(PaymentStatus.Paid, this.store.Payments[paymentEvent.PaymentIntentId].Status);
        Assert.Equal(TransitionOutcome.IgnoredOutOfOrder, Assert.Single(this.store.InboxEvents).Outcome);
        PaymentHistoryEntry entry = Assert.Single(this.store.History);
        Assert.Equal(PaymentStatus.Paid, entry.FromStatus);
        Assert.Equal(PaymentStatus.Created, entry.ToStatus);
        Assert.False(entry.Applied);
        Assert.Equal(ProcessStripeEvent.OutOfOrderReason, entry.Reason);
        Assert.Equal(1, this.store.Commits);
    }

    [Fact]
    public async Task Impossible_conflict_writes_nothing_and_returns_the_reason()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);
        this.Seed(paymentEvent, PaymentStatus.Canceled, amountRefunded: 0);

        ProcessStripeEventResult result = await this.Process(payload);

        Assert.Equal(ProcessStripeEventOutcome.Conflict, result.Outcome);
        Assert.Contains(paymentEvent.EventId, result.Reason, StringComparison.Ordinal);
        Assert.Contains(paymentEvent.PaymentIntentId, result.Reason, StringComparison.Ordinal);
        Assert.Equal(PaymentStatus.Canceled, this.store.Payments[paymentEvent.PaymentIntentId].Status);
        Assert.Empty(this.store.InboxEvents);
        Assert.Empty(this.store.History);
        Assert.Equal(0, this.store.Commits);
        LogEntry log = Assert.Single(this.logger.Entries);
        Assert.Equal(LogLevel.Warning, log.Level);
    }

    [Fact]
    public async Task Concurrency_conflict_is_retried_from_the_read_in_a_new_transaction()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);
        this.store.ConcurrencyConflictsToReport = 1;

        ProcessStripeEventResult result = await this.Process(payload);

        Assert.Equal(new ProcessStripeEventResult(ProcessStripeEventOutcome.Applied), result);
        Assert.Equal(2, this.store.TransactionsStarted);
        Assert.Equal(1, this.store.Commits);
        Assert.Equal(PaymentStatus.Paid, this.store.Payments[paymentEvent.PaymentIntentId].Status);
        Assert.Single(this.store.InboxEvents);
        Assert.Single(this.store.History);
        Assert.Contains(this.logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Concurrency conflict", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Concurrency_conflict_on_every_attempt_writes_nothing_and_returns_the_reason()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);
        this.store.ConcurrencyConflictsToReport = ProcessStripeEvent.MaxAttempts;

        ProcessStripeEventResult result = await this.Process(payload);

        Assert.Equal(ProcessStripeEventOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Contains(paymentEvent.EventId, result.Reason, StringComparison.Ordinal);
        Assert.Equal(ProcessStripeEvent.MaxAttempts, this.store.TransactionsStarted);
        Assert.Equal(0, this.store.Commits);
        Assert.Empty(this.store.Payments);
        Assert.Empty(this.store.InboxEvents);
        Assert.Empty(this.store.History);
        Assert.Contains(this.logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("""{"id":"evt_x","type":"customer.created","created":1,"data":{"object":{}}}""", "customer.created")]
    public async Task Invalid_event_returns_the_parser_message_and_opens_no_transaction(string payload, string expectedReason)
    {
        ProcessStripeEventResult result = await this.Process(payload);

        Assert.Equal(ProcessStripeEventOutcome.InvalidEvent, result.Outcome);
        Assert.Contains(expectedReason, result.Reason, StringComparison.Ordinal);
        Assert.Equal(0, this.store.TransactionsStarted);
        LogEntry log = Assert.Single(this.logger.Entries);
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Contains(expectedReason, log.Message, StringComparison.Ordinal);
    }

    private static string ReadFixture(string fixture)
    {
        return File.ReadAllText(Path.Combine(fixturesDirectory, fixture));
    }

    private void Seed(PaymentEvent paymentEvent, PaymentStatus status, long amountRefunded)
    {
        this.store.Payments.Add(paymentEvent.PaymentIntentId, new Payment(
            paymentEvent.PaymentIntentId,
            status,
            paymentEvent.Amount,
            amountRefunded,
            paymentEvent.Currency,
            "evt_seeded",
            earlierAt,
            earlierAt));
    }

    private Task<ProcessStripeEventResult> Process(string payload)
    {
        ProcessStripeEvent processStripeEvent = new(this.store, new FixedTimeProvider(processedAt), this.logger);
        return processStripeEvent.ExecuteAsync(payload, CancellationToken.None);
    }
}
