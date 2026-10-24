using Payments.Application.Processing;
using Payments.Domain;

namespace Payments.IntegrationTests.Worker;

public sealed class ConflictingPaymentStore : IPaymentStore
{
    private readonly IPaymentStore inner;

    public ConflictingPaymentStore(IPaymentStore inner)
    {
        this.inner = inner;
    }

    public Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken) =>
        this.inner.BeginTransactionAsync(cancellationToken);

    public Task<Payment?> FindPaymentAsync(string paymentIntentId, CancellationToken cancellationToken) =>
        this.inner.FindPaymentAsync(paymentIntentId, cancellationToken);

    public Task<PaymentStoreWriteResult> AddInboxEventAsync(InboxEvent inboxEvent, CancellationToken cancellationToken) =>
        this.inner.AddInboxEventAsync(inboxEvent, cancellationToken);

    public void AddPayment(Payment payment) => this.inner.AddPayment(payment);

    public void AddHistoryEntry(PaymentHistoryEntry entry) => this.inner.AddHistoryEntry(entry);

    public Task<PaymentStoreWriteResult> CommitAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PaymentStoreWriteResult.ConcurrencyConflict);
}
