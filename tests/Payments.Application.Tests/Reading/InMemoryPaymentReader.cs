using Payments.Application.Reading;

namespace Payments.Application.Tests.Reading;

public sealed class InMemoryPaymentReader : IPaymentReader
{
    public List<PaymentSummary> Payments { get; } = [];

    public List<PaymentDetail> Details { get; } = [];

    public PaymentCursor? RequestedAfter { get; private set; }

    public int? RequestedCount { get; private set; }

    public Task<IReadOnlyList<PaymentSummary>> ListAsync(PaymentCursor? after, int count, CancellationToken cancellationToken)
    {
        this.RequestedAfter = after;
        this.RequestedCount = count;
        IReadOnlyList<PaymentSummary> page = this.Payments
            .Where(payment => after is null
                || payment.UpdatedAt < after.UpdatedAt
                || (payment.UpdatedAt == after.UpdatedAt && string.CompareOrdinal(payment.PaymentIntentId, after.PaymentIntentId) < 0))
            .OrderByDescending(payment => payment.UpdatedAt)
            .ThenByDescending(payment => payment.PaymentIntentId, StringComparer.Ordinal)
            .Take(count)
            .ToList();
        return Task.FromResult(page);
    }

    public Task<PaymentDetail?> FindAsync(string paymentIntentId, CancellationToken cancellationToken) =>
        Task.FromResult(this.Details.SingleOrDefault(detail => detail.PaymentIntentId == paymentIntentId));
}
