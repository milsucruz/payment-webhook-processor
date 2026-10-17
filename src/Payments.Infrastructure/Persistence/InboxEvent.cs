using Payments.Domain;

namespace Payments.Infrastructure.Persistence;

public sealed class InboxEvent
{
    public required string EventId { get; init; }

    public required string EventType { get; init; }

    public required string PaymentIntentId { get; init; }

    public required DateTimeOffset StripeCreatedAt { get; init; }

    public required DateTimeOffset ReceivedAt { get; init; }

    public required TransitionOutcome Outcome { get; init; }

    public string? TraceId { get; init; }

    public required string Payload { get; init; }
}
