using Payments.Application.Processing;

namespace Payments.Api.Hubs;

public sealed record EventProcessedMessage(
    string EventId,
    string EventType,
    string PaymentIntentId,
    ProcessStripeEventOutcome Outcome,
    string? TraceId);
