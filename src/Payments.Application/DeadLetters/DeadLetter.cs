namespace Payments.Application.DeadLetters;

public sealed record DeadLetter(
    long SequenceNumber,
    string EventId,
    string? Reason,
    string? Description,
    int DeliveryCount,
    DateTimeOffset EnqueuedAt);
