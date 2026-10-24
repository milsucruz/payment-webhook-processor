using Payments.Application.Processing;
using Payments.Application.Reading;

namespace Payments.Application.Notifications;

public sealed record PaymentNotification(
    string EventId,
    string EventType,
    string PaymentIntentId,
    ProcessStripeEventOutcome Outcome,
    PaymentSummary? Payment);
