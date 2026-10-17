using Microsoft.Extensions.Logging;
using Payments.Application.Events;
using Payments.Domain;

namespace Payments.Application.Processing;

// Details: docs/code/ProcessStripeEvent.md
public class ProcessStripeEvent
{
    public const string OutOfOrderReason = "out of order";
    public const string RedundantReason = "redundant";
    public const int MaxAttempts = 3;

    private readonly IPaymentStore paymentStore;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<ProcessStripeEvent> logger;

    public ProcessStripeEvent(IPaymentStore paymentStore, TimeProvider timeProvider, ILogger<ProcessStripeEvent> logger)
    {
        this.paymentStore = paymentStore;
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

        for (int attempt = 1; ; attempt++)
        {
            ProcessStripeEventResult result = await this.ProcessOnceAsync(paymentEvent, payload, cancellationToken);
            if (result.Outcome != ProcessStripeEventOutcome.ConcurrencyConflict)
            {
                return result;
            }

            if (attempt == MaxAttempts)
            {
                string reason = $"Event {paymentEvent.EventId} for payment {paymentEvent.PaymentIntentId} hit a concurrency conflict on all {MaxAttempts} attempts.";
                this.logger.LogWarning("Concurrency conflict: {Reason}", reason);
                return new ProcessStripeEventResult(ProcessStripeEventOutcome.ConcurrencyConflict, reason);
            }

            this.logger.LogInformation(
                "Concurrency conflict on payment {PaymentIntentId} for event {EventId}, retrying (attempt {Attempt} of {MaxAttempts})",
                paymentEvent.PaymentIntentId,
                paymentEvent.EventId,
                attempt,
                MaxAttempts);
        }
    }

    private async Task<ProcessStripeEventResult> ProcessOnceAsync(PaymentEvent paymentEvent, string payload, CancellationToken cancellationToken)
    {
        DateTimeOffset processedAt = this.timeProvider.GetUtcNow();
        await using IAsyncDisposable transaction = await this.paymentStore.BeginTransactionAsync(cancellationToken);

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
