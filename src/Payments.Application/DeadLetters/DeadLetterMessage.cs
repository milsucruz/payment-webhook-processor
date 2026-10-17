namespace Payments.Application.DeadLetters;

public sealed record DeadLetterMessage(
    long SequenceNumber,
    string MessageId,
    string? Reason,
    string? ErrorDescription,
    string? AbandonReason,
    int DeliveryCount,
    DateTimeOffset EnqueuedAt);
