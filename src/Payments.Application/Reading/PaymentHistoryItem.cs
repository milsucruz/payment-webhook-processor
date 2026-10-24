using Payments.Domain;

namespace Payments.Application.Reading;

public sealed record PaymentHistoryItem(
    string EventId,
    string EventType,
    PaymentStatus? FromStatus,
    PaymentStatus ToStatus,
    bool Applied,
    string? Reason,
    DateTimeOffset OccurredAt,
    string? TraceId);
