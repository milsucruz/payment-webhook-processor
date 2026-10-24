using Payments.Domain;

namespace Payments.Application.Reading;

public sealed record PaymentDetail(
    string PaymentIntentId,
    PaymentStatus Status,
    long Amount,
    long AmountRefunded,
    string Currency,
    string LastEventId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PaymentHistoryItem> History);
