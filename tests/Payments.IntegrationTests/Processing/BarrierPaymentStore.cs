using Payments.Application.Processing;
using Payments.Domain;

namespace Payments.IntegrationTests.Processing;

public sealed class BarrierPaymentStore : IPaymentStore
{
    private readonly IPaymentStore inner;
    private readonly ReadBarrier barrier;
    private bool firstReadDone;

    public BarrierPaymentStore(IPaymentStore inner, ReadBarrier barrier)
    {
        this.inner = inner;
        this.barrier = barrier;
    }

    public int ConcurrencyConflicts { get; private set; }

    public Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        return this.inner.BeginTransactionAsync(cancellationToken);
    }

    public async Task<Payment?> FindPaymentAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        Payment? payment = await this.inner.FindPaymentAsync(paymentIntentId, cancellationToken);
        if (!this.firstReadDone)
        {
            this.firstReadDone = true;
            await this.barrier.SignalAndWaitAsync(cancellationToken);
        }

        return payment;
    }

    public async Task<PaymentStoreWriteResult> AddInboxEventAsync(InboxEvent inboxEvent, CancellationToken cancellationToken)
    {
        return this.Count(await this.inner.AddInboxEventAsync(inboxEvent, cancellationToken));
    }

    public void AddPayment(Payment payment)
    {
        this.inner.AddPayment(payment);
    }

    public void AddHistoryEntry(PaymentHistoryEntry entry)
    {
        this.inner.AddHistoryEntry(entry);
    }

    public async Task<PaymentStoreWriteResult> CommitAsync(CancellationToken cancellationToken)
    {
        return this.Count(await this.inner.CommitAsync(cancellationToken));
    }

    private PaymentStoreWriteResult Count(PaymentStoreWriteResult result)
    {
        if (result == PaymentStoreWriteResult.ConcurrencyConflict)
        {
            this.ConcurrencyConflicts++;
        }

        return result;
    }
}
