using Payments.Domain;

namespace Payments.Infrastructure.Persistence;

public sealed class PaymentHistoryEntry
{
    public long Id { get; init; }

    public required string PaymentIntentId { get; init; }

    public required string EventId { get; init; }

    public PaymentStatus? FromStatus { get; init; }

    public required PaymentStatus ToStatus { get; init; }

    public required bool Applied { get; init; }

    public string? Reason { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
}
