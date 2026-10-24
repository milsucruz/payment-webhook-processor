using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Payments.Application.Events;
using Payments.Application.Notifications;
using Payments.Application.Reading;
using Payments.Application.Telemetry;
using Payments.Domain;

namespace Payments.Application.Processing;

// Details: docs/code/ProcessStripeEvent.md
public class ProcessStripeEvent
{
    public const string ActivitySourceName = "Payments.Processing";

    private const string OutOfOrderReason = "out of order";
    private const string RedundantReason = "redundant";
    private const int MaxConcurrencyAttempts = 3;
    private const string TransactionActivityName = "payment transaction";

    private static readonly ActivitySource activitySource = new(ActivitySourceName);

    private static readonly IReadOnlyList<TimeSpan> transientRetryDelays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];

    private readonly IPaymentStore paymentStore;
    private readonly ITransientErrorDetector transientErrorDetector;
    private readonly IFaultInjector faultInjector;
    private readonly IPaymentNotificationPublisher notificationPublisher;
    private readonly TimeProvider timeProvider;
    private readonly PaymentsMetrics metrics;
    private readonly ILogger<ProcessStripeEvent> logger;

    public ProcessStripeEvent(
        IPaymentStore paymentStore,
        ITransientErrorDetector transientErrorDetector,
        IFaultInjector faultInjector,
        IPaymentNotificationPublisher notificationPublisher,
        TimeProvider timeProvider,
        PaymentsMetrics metrics,
        ILogger<ProcessStripeEvent> logger)
    {
        this.paymentStore = paymentStore;
        this.transientErrorDetector = transientErrorDetector;
        this.faultInjector = faultInjector;
        this.notificationPublisher = notificationPublisher;
        this.timeProvider = timeProvider;
        this.metrics = metrics;
        this.logger = logger;
    }

    public async Task<ProcessStripeEventResult> ExecuteAsync(string payload, DateTimeOffset receivedAt, CancellationToken cancellationToken)
    {
        PaymentEvent paymentEvent;
        try
        {
            paymentEvent = StripeEventParser.Parse(payload);
        }
        catch (InvalidStripeEventException exception)
        {
            this.logger.LogWarning("Rejected Stripe event: {Reason}", exception.Message);
            return new ProcessStripeEventResult(ProcessStripeEventOutcome.InvalidEvent, exception.Message);
        }

        int attempt = 0;
        int concurrencyAttempts = 0;
        int transientRetries = 0;
        while (true)
        {
            attempt++;
            AttemptResult attemptResult;
            try
            {
                attemptResult = await this.ProcessOnceAsync(paymentEvent, payload, attempt, cancellationToken);
            }
            catch (Exception exception) when (this.transientErrorDetector.IsTransient(exception))
            {
                if (transientRetries == transientRetryDelays.Count)
                {
                    string reason = $"Event {paymentEvent.EventId} for payment {paymentEvent.PaymentIntentId} failed with a transient error on all {transientRetryDelays.Count + 1} attempts: {exception.Message}";
                    this.logger.LogWarning(exception, "Transient error: {Reason}", reason);
                    return new ProcessStripeEventResult(ProcessStripeEventOutcome.TransientFailure, reason);
                }

                TimeSpan delay = transientRetryDelays[transientRetries];
                transientRetries++;
                this.logger.LogWarning(
                    "Transient error on payment {PaymentIntentId} for event {EventId}, retrying in {Delay} (retry {Retry} of {MaxRetries}): {Error}",
                    paymentEvent.PaymentIntentId,
                    paymentEvent.EventId,
                    delay,
                    transientRetries,
                    transientRetryDelays.Count,
                    exception.Message);
                await Task.Delay(delay, this.timeProvider, cancellationToken);
                continue;
            }

            if (attemptResult.Result.Outcome != ProcessStripeEventOutcome.ConcurrencyConflict)
            {
                this.metrics.EventProcessed(attemptResult.Result.Outcome, this.timeProvider.GetUtcNow() - receivedAt);
                await this.NotifyAsync(paymentEvent, attemptResult, cancellationToken);
                return attemptResult.Result;
            }

            concurrencyAttempts++;
            if (concurrencyAttempts == MaxConcurrencyAttempts)
            {
                string reason = $"Event {paymentEvent.EventId} for payment {paymentEvent.PaymentIntentId} hit a concurrency conflict on all {MaxConcurrencyAttempts} attempts.";
                this.logger.LogWarning("Concurrency conflict: {Reason}", reason);
                return new ProcessStripeEventResult(ProcessStripeEventOutcome.ConcurrencyConflict, reason);
            }

            this.logger.LogInformation(
                "Concurrency conflict on payment {PaymentIntentId} for event {EventId}, retrying (attempt {Attempt} of {MaxAttempts})",
                paymentEvent.PaymentIntentId,
                paymentEvent.EventId,
                concurrencyAttempts,
                MaxConcurrencyAttempts);
        }
    }

    private async Task NotifyAsync(PaymentEvent paymentEvent, AttemptResult attemptResult, CancellationToken cancellationToken)
    {
        if (attemptResult.Result.Outcome is not (ProcessStripeEventOutcome.Applied
            or ProcessStripeEventOutcome.IgnoredOutOfOrder
            or ProcessStripeEventOutcome.Redundant
            or ProcessStripeEventOutcome.Duplicate))
        {
            return;
        }

        PaymentNotification notification = new(
            paymentEvent.EventId,
            paymentEvent.EventType,
            paymentEvent.PaymentIntentId,
            attemptResult.Result.Outcome,
            attemptResult.Payment,
            Activity.Current?.TraceId.ToHexString());
        try
        {
            await this.notificationPublisher.PublishAsync(notification, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            this.logger.LogWarning(
                exception,
                "Notification for event {EventId} on payment {PaymentIntentId} was not published",
                paymentEvent.EventId,
                paymentEvent.PaymentIntentId);
        }
    }

    private async Task<AttemptResult> ProcessOnceAsync(PaymentEvent paymentEvent, string payload, int attempt, CancellationToken cancellationToken)
    {
        using Activity? activity = activitySource.StartActivity(TransactionActivityName);
        activity?.SetTag("payments.event_id", paymentEvent.EventId);
        activity?.SetTag("payments.payment_intent_id", paymentEvent.PaymentIntentId);
        activity?.SetTag("payments.attempt", attempt);
        try
        {
            await this.faultInjector.InjectAsync(paymentEvent, attempt, cancellationToken);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            throw;
        }

        DateTimeOffset processedAt = this.timeProvider.GetUtcNow();
        IAsyncDisposable transaction = await this.paymentStore.BeginTransactionAsync(cancellationToken);
        AttemptResult result;
        try
        {
            result = await this.DecideAndSaveAsync(paymentEvent, payload, processedAt, cancellationToken);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            await this.DisposeFailedTransactionAsync(transaction, paymentEvent);
            throw;
        }

        await transaction.DisposeAsync();
        activity?.SetTag("payments.outcome", result.Result.Outcome.ToString());
        return result;
    }

    private async Task DisposeFailedTransactionAsync(IAsyncDisposable transaction, PaymentEvent paymentEvent)
    {
        try
        {
            await transaction.DisposeAsync();
        }
        catch (Exception exception)
        {
            this.logger.LogWarning(
                "Rollback failed after a failed attempt on payment {PaymentIntentId} for event {EventId}: {Error}",
                paymentEvent.PaymentIntentId,
                paymentEvent.EventId,
                exception.Message);
        }
    }

    private async Task<AttemptResult> DecideAndSaveAsync(PaymentEvent paymentEvent, string payload, DateTimeOffset processedAt, CancellationToken cancellationToken)
    {
        Payment? existingPayment = await this.paymentStore.FindPaymentAsync(paymentEvent.PaymentIntentId, cancellationToken);
        PaymentStatus? fromStatus = existingPayment?.Status;
        PaymentSummary? committedPayment = existingPayment is null ? null : Summarize(existingPayment);
        TransitionOutcome outcome = existingPayment is null
            ? TransitionOutcome.Applied
            : existingPayment.Apply(paymentEvent.RequestedStatus, paymentEvent.AmountRefunded, paymentEvent.EventId, processedAt);

        if (outcome == TransitionOutcome.Conflict)
        {
            string reason = $"Event {paymentEvent.EventId} requests {paymentEvent.RequestedStatus} for payment {paymentEvent.PaymentIntentId}, which is {fromStatus}.";
            this.logger.LogWarning("Impossible conflict: {Reason}", reason);
            return new AttemptResult(new ProcessStripeEventResult(ProcessStripeEventOutcome.Conflict, reason));
        }

        InboxEvent inboxEvent = new()
        {
            EventId = paymentEvent.EventId,
            EventType = paymentEvent.EventType,
            PaymentIntentId = paymentEvent.PaymentIntentId,
            StripeCreatedAt = paymentEvent.StripeCreatedAt,
            ReceivedAt = processedAt,
            Outcome = outcome,
            Payload = payload,
            TraceId = Activity.Current?.TraceId.ToHexString(),
        };
        PaymentStoreWriteResult inboxResult = await this.paymentStore.AddInboxEventAsync(inboxEvent, cancellationToken);
        if (inboxResult == PaymentStoreWriteResult.DuplicateEvent)
        {
            this.logger.LogInformation("Event {EventId} was already processed", paymentEvent.EventId);
            return new AttemptResult(new ProcessStripeEventResult(ProcessStripeEventOutcome.Duplicate), committedPayment);
        }

        if (inboxResult == PaymentStoreWriteResult.ConcurrencyConflict)
        {
            return new AttemptResult(new ProcessStripeEventResult(ProcessStripeEventOutcome.ConcurrencyConflict));
        }

        Payment payment = existingPayment ?? new Payment(
            paymentEvent.PaymentIntentId,
            paymentEvent.RequestedStatus,
            paymentEvent.Amount,
            paymentEvent.AmountRefunded,
            paymentEvent.Currency,
            paymentEvent.EventId,
            processedAt,
            processedAt);
        if (existingPayment is null)
        {
            this.paymentStore.AddPayment(payment);
        }

        this.paymentStore.AddHistoryEntry(new PaymentHistoryEntry
        {
            PaymentIntentId = paymentEvent.PaymentIntentId,
            EventId = paymentEvent.EventId,
            FromStatus = fromStatus,
            ToStatus = paymentEvent.RequestedStatus,
            Applied = outcome == TransitionOutcome.Applied,
            Reason = HistoryReason(outcome),
            OccurredAt = processedAt,
        });

        if (await this.paymentStore.CommitAsync(cancellationToken) == PaymentStoreWriteResult.ConcurrencyConflict)
        {
            return new AttemptResult(new ProcessStripeEventResult(ProcessStripeEventOutcome.ConcurrencyConflict));
        }

        this.logger.LogInformation(
            "Event {EventId} for payment {PaymentIntentId}: {Outcome}",
            paymentEvent.EventId,
            paymentEvent.PaymentIntentId,
            outcome);
        return new AttemptResult(new ProcessStripeEventResult(ToProcessOutcome(outcome)), Summarize(payment));
    }

    private static PaymentSummary Summarize(Payment payment) =>
        new(payment.PaymentIntentId, payment.Status, payment.Amount, payment.AmountRefunded, payment.Currency, payment.UpdatedAt);

    private static string? HistoryReason(TransitionOutcome outcome)
    {
        return outcome switch
        {
            TransitionOutcome.IgnoredOutOfOrder => OutOfOrderReason,
            TransitionOutcome.Redundant => RedundantReason,
            _ => null,
        };
    }

    private static ProcessStripeEventOutcome ToProcessOutcome(TransitionOutcome outcome)
    {
        return outcome switch
        {
            TransitionOutcome.Applied => ProcessStripeEventOutcome.Applied,
            TransitionOutcome.IgnoredOutOfOrder => ProcessStripeEventOutcome.IgnoredOutOfOrder,
            TransitionOutcome.Redundant => ProcessStripeEventOutcome.Redundant,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unexpected transition outcome."),
        };
    }

    private sealed record AttemptResult(ProcessStripeEventResult Result, PaymentSummary? Payment = null);
}
