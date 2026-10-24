using Microsoft.Extensions.Logging;
using Payments.Application.Events;
using Payments.Application.Notifications;
using Payments.Application.Processing;
using Payments.Application.Reading;
using Payments.Application.Tests.Notifications;
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
    private readonly TestTransientErrorDetector transientErrorDetector = new();
    private readonly TestFaultInjector faultInjector = new();
    private readonly RecordingNotificationPublisher notificationPublisher = new();
    private readonly DelayTrackingTimeProvider timeProvider = new(processedAt);

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
        Assert.Equal("redundant", entry.Reason);
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
        Assert.Equal("out of order", entry.Reason);
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
        this.store.ConcurrencyConflictsToReport = 3;

        ProcessStripeEventResult result = await this.Process(payload);

        Assert.Equal(ProcessStripeEventOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Contains(paymentEvent.EventId, result.Reason, StringComparison.Ordinal);
        Assert.Equal(3, this.store.TransactionsStarted);
        Assert.Equal(0, this.store.Commits);
        Assert.Empty(this.store.Payments);
        Assert.Empty(this.store.InboxEvents);
        Assert.Empty(this.store.History);
        Assert.Contains(this.logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Transient_error_on_two_attempts_is_retried_and_the_third_attempt_applies_the_event()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);
        this.store.CommitExceptions.Enqueue(new TransientTestException());
        this.store.CommitExceptions.Enqueue(new TransientTestException());

        ProcessStripeEventResult result = await this.ProcessThroughDelays(payload, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

        Assert.Equal(new ProcessStripeEventResult(ProcessStripeEventOutcome.Applied), result);
        Assert.Equal(3, this.store.TransactionsStarted);
        Assert.Equal(1, this.store.Commits);
        Assert.Equal(PaymentStatus.Paid, this.store.Payments[paymentEvent.PaymentIntentId].Status);
        Assert.Single(this.store.InboxEvents);
        Assert.Single(this.store.History);
        Assert.Equal(2, this.logger.Entries.Count(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Transient error", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Transient_error_on_every_attempt_writes_nothing_and_returns_transient_failure_after_three_retries()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            this.store.CommitExceptions.Enqueue(new TransientTestException());
        }

        ProcessStripeEventResult result = await this.ProcessThroughDelays(
            payload,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15));

        Assert.Equal(ProcessStripeEventOutcome.TransientFailure, result.Outcome);
        Assert.Contains(paymentEvent.EventId, result.Reason, StringComparison.Ordinal);
        Assert.Equal(4, this.store.TransactionsStarted);
        Assert.Equal(0, this.store.Commits);
        Assert.Empty(this.store.Payments);
        Assert.Empty(this.store.InboxEvents);
        Assert.Empty(this.store.History);
    }

    [Fact]
    public async Task Cancellation_during_a_transient_retry_delay_stops_without_another_attempt()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        this.store.CommitExceptions.Enqueue(new TransientTestException());
        using CancellationTokenSource cancellation = new();

        Task<ProcessStripeEventResult> processing = this.Process(payload, cancellation.Token);
        Assert.Equal(TimeSpan.FromSeconds(1), await this.timeProvider.WaitForDelay());
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        Assert.Equal(1, this.store.TransactionsStarted);
        Assert.Equal(0, this.store.Commits);
    }

    [Fact]
    public async Task Exception_that_is_not_transient_propagates_without_retry()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        this.store.CommitExceptions.Enqueue(new InvalidOperationException("not transient"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => this.Process(payload));

        Assert.Equal(1, this.store.TransactionsStarted);
        Assert.False(this.timeProvider.HasUnobservedDelay);
    }

    [Fact]
    public async Task Transient_retries_and_concurrency_attempts_have_separate_limits()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        this.store.CommitExceptions.Enqueue(new TransientTestException());
        this.store.ConcurrencyConflictsToReport = 2;

        ProcessStripeEventResult result = await this.ProcessThroughDelays(payload, TimeSpan.FromSeconds(1));

        Assert.Equal(new ProcessStripeEventResult(ProcessStripeEventOutcome.Applied), result);
        Assert.Equal(4, this.store.TransactionsStarted);
        Assert.Equal(1, this.store.Commits);
    }

    [Fact]
    public async Task Fault_injector_runs_before_each_attempt_with_the_attempt_number()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);
        this.faultInjector.Exceptions.Enqueue(new TransientTestException());
        this.faultInjector.Exceptions.Enqueue(new TransientTestException());

        ProcessStripeEventResult result = await this.ProcessThroughDelays(payload, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

        Assert.Equal(new ProcessStripeEventResult(ProcessStripeEventOutcome.Applied), result);
        Assert.Equal([(paymentEvent.EventId, 1), (paymentEvent.EventId, 2), (paymentEvent.EventId, 3)], this.faultInjector.Calls);
        Assert.Equal(1, this.store.TransactionsStarted);
        Assert.Equal(1, this.store.Commits);
        Assert.Single(this.store.InboxEvents);
    }

    [Fact]
    public async Task Time_spent_in_the_fault_injector_is_not_hidden_in_the_recorded_times()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        this.faultInjector.OnInject = () => this.timeProvider.Advance(TimeSpan.FromSeconds(10));

        await this.Process(payload);

        DateTimeOffset expectedAt = processedAt.AddSeconds(10);
        Assert.Equal(expectedAt, Assert.Single(this.store.InboxEvents).ReceivedAt);
        Assert.Equal(expectedAt, Assert.Single(this.store.Payments.Values).UpdatedAt);
        Assert.Equal(expectedAt, Assert.Single(this.store.History).OccurredAt);
    }

    [Fact]
    public async Task Fault_injector_exception_that_is_not_transient_propagates_and_writes_nothing()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        this.faultInjector.Exceptions.Enqueue(new InvalidOperationException("poison"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => this.Process(payload));

        Assert.Equal(0, this.store.TransactionsStarted);
        Assert.Equal(0, this.store.Commits);
        Assert.Empty(this.store.Payments);
        Assert.Empty(this.store.InboxEvents);
        Assert.Empty(this.store.History);
    }

    [Fact]
    public async Task Concurrency_retry_counts_as_a_new_attempt_for_the_fault_injector()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        this.store.ConcurrencyConflictsToReport = 1;

        await this.Process(payload);

        Assert.Equal([1, 2], this.faultInjector.Calls.Select(call => call.Attempt));
    }

    [Fact]
    public async Task Invalid_event_never_reaches_the_fault_injector()
    {
        await this.Process("not json");

        Assert.Empty(this.faultInjector.Calls);
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

    [Fact]
    public async Task Applied_event_publishes_a_notification_with_the_payment_after_the_commit()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);
        int commitsAtPublish = -1;
        this.notificationPublisher.OnPublish = () => commitsAtPublish = this.store.Commits;

        await this.Process(payload);

        Assert.Equal(1, commitsAtPublish);
        PaymentNotification notification = Assert.Single(this.notificationPublisher.Published);
        Assert.Equal(paymentEvent.EventId, notification.EventId);
        Assert.Equal(paymentEvent.EventType, notification.EventType);
        Assert.Equal(paymentEvent.PaymentIntentId, notification.PaymentIntentId);
        Assert.Equal(ProcessStripeEventOutcome.Applied, notification.Outcome);
        Assert.Equal(
            new PaymentSummary(paymentEvent.PaymentIntentId, PaymentStatus.Paid, paymentEvent.Amount, 0, paymentEvent.Currency, processedAt),
            notification.Payment);
    }

    [Fact]
    public async Task Ignored_event_publishes_a_notification_with_the_unchanged_payment()
    {
        string payload = ReadFixture("payment_intent.created.json");
        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);
        this.Seed(paymentEvent, PaymentStatus.Paid, amountRefunded: 0);

        await this.Process(payload);

        PaymentNotification notification = Assert.Single(this.notificationPublisher.Published);
        Assert.Equal(ProcessStripeEventOutcome.IgnoredOutOfOrder, notification.Outcome);
        Assert.Equal(
            new PaymentSummary(paymentEvent.PaymentIntentId, PaymentStatus.Paid, paymentEvent.Amount, 0, paymentEvent.Currency, earlierAt),
            notification.Payment);
    }

    [Fact]
    public async Task Redundant_event_publishes_a_notification()
    {
        string payload = ReadFixture("charge.refunded.partial.json");
        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);
        this.Seed(paymentEvent, PaymentStatus.PartiallyRefunded, amountRefunded: paymentEvent.AmountRefunded);

        await this.Process(payload);

        PaymentNotification notification = Assert.Single(this.notificationPublisher.Published);
        Assert.Equal(ProcessStripeEventOutcome.Redundant, notification.Outcome);
        Assert.Equal(PaymentStatus.PartiallyRefunded, notification.Payment?.Status);
    }

    [Fact]
    public async Task Duplicate_event_publishes_a_notification_without_the_payment()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);
        await this.Process(payload);

        await this.Process(payload);

        Assert.Equal(2, this.notificationPublisher.Published.Count);
        PaymentNotification notification = this.notificationPublisher.Published[1];
        Assert.Equal(paymentEvent.EventId, notification.EventId);
        Assert.Equal(ProcessStripeEventOutcome.Duplicate, notification.Outcome);
        Assert.Null(notification.Payment);
    }

    [Fact]
    public async Task Concurrency_conflict_retried_publishes_one_notification()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        this.store.ConcurrencyConflictsToReport = 1;

        await this.Process(payload);

        Assert.Equal(ProcessStripeEventOutcome.Applied, Assert.Single(this.notificationPublisher.Published).Outcome);
    }

    [Fact]
    public async Task Impossible_conflict_publishes_no_notification()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        this.Seed(StripeEventParser.Parse(payload), PaymentStatus.Canceled, amountRefunded: 0);

        await this.Process(payload);

        Assert.Empty(this.notificationPublisher.Published);
    }

    [Fact]
    public async Task Invalid_event_publishes_no_notification()
    {
        await this.Process("not json");

        Assert.Empty(this.notificationPublisher.Published);
    }

    [Fact]
    public async Task Concurrency_conflict_on_every_attempt_publishes_no_notification()
    {
        this.store.ConcurrencyConflictsToReport = 3;

        await this.Process(ReadFixture("payment_intent.succeeded.json"));

        Assert.Empty(this.notificationPublisher.Published);
    }

    [Fact]
    public async Task Transient_failure_publishes_no_notification()
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            this.store.CommitExceptions.Enqueue(new TransientTestException());
        }

        await this.ProcessThroughDelays(
            ReadFixture("payment_intent.succeeded.json"),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15));

        Assert.Empty(this.notificationPublisher.Published);
    }

    [Fact]
    public async Task Failed_notification_is_logged_and_keeps_the_outcome()
    {
        string payload = ReadFixture("payment_intent.succeeded.json");
        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);
        this.notificationPublisher.FailWith = new InvalidOperationException("Service Bus is down");

        ProcessStripeEventResult result = await this.Process(payload);

        Assert.Equal(new ProcessStripeEventResult(ProcessStripeEventOutcome.Applied), result);
        Assert.Equal(1, this.store.Commits);
        Assert.Equal(1, this.store.TransactionsStarted);
        Assert.Contains(
            this.logger.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Notification", StringComparison.Ordinal) && entry.Message.Contains(paymentEvent.EventId, StringComparison.Ordinal));
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

    private Task<ProcessStripeEventResult> Process(string payload, CancellationToken cancellationToken = default)
    {
        ProcessStripeEvent processStripeEvent = new(this.store, this.transientErrorDetector, this.faultInjector, this.notificationPublisher, this.timeProvider, this.logger);
        return processStripeEvent.ExecuteAsync(payload, cancellationToken);
    }

    private async Task<ProcessStripeEventResult> ProcessThroughDelays(string payload, params TimeSpan[] expectedDelays)
    {
        Task<ProcessStripeEventResult> processing = this.Process(payload);
        foreach (TimeSpan expectedDelay in expectedDelays)
        {
            Assert.Equal(expectedDelay, await this.timeProvider.WaitForDelay());
            this.timeProvider.Advance(expectedDelay);
        }

        ProcessStripeEventResult result = await processing;
        Assert.False(this.timeProvider.HasUnobservedDelay);
        return result;
    }
}
