using Microsoft.Extensions.Logging;
using Payments.Application.Events;
using Payments.Domain;

namespace Payments.Application.Processing;

// Details: docs/code/ProcessStripeEvent.md
public class ProcessStripeEvent
{
    private const string OutOfOrderReason = "out of order";
    private const string RedundantReason = "redundant";
    private const int MaxConcurrencyAttempts = 3;

    private static readonly IReadOnlyList<TimeSpan> transientRetryDelays =
        [TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)];

    private readonly IPaymentStore paymentStore;
    private readonly ITransientErrorDetector transientErrorDetector;
    private readonly IFaultInjector faultInjector;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<ProcessStripeEvent> logger;

    public ProcessStripeEvent(
        IPaymentStore paymentStore,
        ITransientErrorDetector transientErrorDetector,
        IFaultInjector faultInjector,
        TimeProvider timeProvider,
        ILogger<ProcessStripeEvent> logger)
    {
        this.paymentStore = paymentStore;
        this.transientErrorDetector = transientErrorDetector;
        this.faultInjector = faultInjector;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public async Task<ProcessStripeEventResult> ExecuteAsync(string payload, CancellationToken cancellationToken)
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
            ProcessStripeEventResult result;
            try
            {
                result = await this.ProcessOnceAsync(paymentEvent, payload, attempt, cancellationToken);
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
                    exception,
                    "Transient error on payment {PaymentIntentId} for event {EventId}, retrying in {Delay} (retry {Retry} of {MaxRetries})",
                    paymentEvent.PaymentIntentId,
                    paymentEvent.EventId,
                    delay,
                    transientRetries,
                    transientRetryDelays.Count);
                await Task.Delay(delay, this.timeProvider, cancellationToken);
                continue;
            }

            if (result.Outcome != ProcessStripeEventOutcome.ConcurrencyConflict)
            {
                return result;
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

    private async Task<ProcessStripeEventResult> ProcessOnceAsync(PaymentEvent paymentEvent, string payload, int attempt, CancellationToken cancellationToken)
    {
        await this.faultInjector.InjectAsync(paymentEvent, attempt, cancellationToken);
        DateTimeOffset processedAt = this.timeProvider.GetUtcNow();
        IAsyncDisposable transaction = await this.paymentStore.BeginTransactionAsync(cancellationToken);
        ProcessStripeEventResult result;
        try
        {
            result = await this.DecideAndSaveAsync(paymentEvent, payload, processedAt, cancellationToken);
        }
        catch
        {
            await this.DisposeFailedTransactionAsync(transaction, paymentEvent);
            throw;
        }

        await transaction.DisposeAsync();
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
                exception,
                "Rollback failed after a failed attempt on payment {PaymentIntentId} for event {EventId}",
                paymentEvent.PaymentIntentId,
                paymentEvent.EventId);
        }
    }

    private async Task<ProcessStripeEventResult> DecideAndSaveAsync(PaymentEvent paymentEvent, string payload, DateTimeOffset processedAt, CancellationToken cancellationToken)
    {
        Payment? existingPayment = await this.paymentStore.FindPaymentAsync(paymentEvent.PaymentIntentId, cancellationToken);
        PaymentStatus? fromStatus = existingPayment?.Status;
        TransitionOutcome outcome = existingPayment is null
            ? TransitionOutcome.Applied
            : existingPayment.Apply(paymentEvent.RequestedStatus, paymentEvent.AmountRefunded, paymentEvent.EventId, processedAt);

        if (outcome == TransitionOutcome.Conflict)
        {
            string reason = $"Event {paymentEvent.EventId} requests {paymentEvent.RequestedStatus} for payment {paymentEvent.PaymentIntentId}, which is {fromStatus}.";
            this.logger.LogWarning("Impossible conflict: {Reason}", reason);
            return new ProcessStripeEventResult(ProcessStripeEventOutcome.Conflict, reason);
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
        };
        PaymentStoreWriteResult inboxResult = await this.paymentStore.AddInboxEventAsync(inboxEvent, cancellationToken);
        if (inboxResult == PaymentStoreWriteResult.DuplicateEvent)
        {
            this.logger.LogInformation("Event {EventId} was already processed", paymentEvent.EventId);
            return new ProcessStripeEventResult(ProcessStripeEventOutcome.Duplicate);
        }

        if (inboxResult == PaymentStoreWriteResult.ConcurrencyConflict)
        {
            return new ProcessStripeEventResult(ProcessStripeEventOutcome.ConcurrencyConflict);
        }

        if (existingPayment is null)
        {
            this.paymentStore.AddPayment(new Payment(
                paymentEvent.PaymentIntentId,
                paymentEvent.RequestedStatus,
                paymentEvent.Amount,
                paymentEvent.AmountRefunded,
                paymentEvent.Currency,
                paymentEvent.EventId,
                processedAt,
                processedAt));
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
            return new ProcessStripeEventResult(ProcessStripeEventOutcome.ConcurrencyConflict);
        }

        this.logger.LogInformation(
            "Event {EventId} for payment {PaymentIntentId}: {Outcome}",
            paymentEvent.EventId,
            paymentEvent.PaymentIntentId,
            outcome);
        return new ProcessStripeEventResult(ToProcessOutcome(outcome));
    }

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
}
