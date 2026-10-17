using Payments.Domain;

namespace Payments.Application.Processing;

public interface IPaymentStore
{
    Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken);

    Task<Payment?> FindPaymentAsync(string paymentIntentId, CancellationToken cancellationToken);

    Task<PaymentStoreWriteResult> AddInboxEventAsync(InboxEvent inboxEvent, CancellationToken cancellationToken);

    void AddPayment(Payment payment);

    void AddHistoryEntry(PaymentHistoryEntry entry);

    Task<PaymentStoreWriteResult> CommitAsync(CancellationToken cancellationToken);
}
