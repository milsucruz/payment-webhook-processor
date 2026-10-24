namespace Payments.Application.DeadLetters;

public sealed record DeadLetter(
    long SequenceNumber,
    string EventId,
    string? Reason,
    string? Description,
    string? LastFailureReason,
    int DeliveryCount,
    DateTimeOffset EnqueuedAt);
