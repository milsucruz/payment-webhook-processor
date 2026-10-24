using Payments.Domain;

namespace Payments.Application.Reading;

public sealed record PaymentSummary(
    string PaymentIntentId,
    PaymentStatus Status,
    long Amount,
    long AmountRefunded,
    string Currency,
    DateTimeOffset UpdatedAt);
