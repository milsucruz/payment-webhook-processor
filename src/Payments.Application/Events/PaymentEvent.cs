using Payments.Domain;

namespace Payments.Application.Events;

public sealed record PaymentEvent(
    string EventId,
    string EventType,
    DateTimeOffset StripeCreatedAt,
    string PaymentIntentId,
    PaymentStatus RequestedStatus,
    long Amount,
    long AmountRefunded,
    string Currency);
