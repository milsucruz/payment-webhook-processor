using Payments.Application.Processing;
using Payments.Domain;

namespace Payments.IntegrationTests.Worker;

public sealed class CrashAfterCommitPaymentStore : IPaymentStore
{
    private readonly IPaymentStore inner;
    private readonly CrashSwitch crashSwitch;

    public CrashAfterCommitPaymentStore(IPaymentStore inner, CrashSwitch crashSwitch)
    {
        this.inner = inner;
        this.crashSwitch = crashSwitch;
    }

    public Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken) =>
        this.inner.BeginTransactionAsync(cancellationToken);

    public Task<Payment?> FindPaymentAsync(string paymentIntentId, CancellationToken cancellationToken) =>
        this.inner.FindPaymentAsync(paymentIntentId, cancellationToken);

    public Task<PaymentStoreWriteResult> AddInboxEventAsync(InboxEvent inboxEvent, CancellationToken cancellationToken) =>
        this.inner.AddInboxEventAsync(inboxEvent, cancellationToken);

    public void AddPayment(Payment payment) => this.inner.AddPayment(payment);

    public void AddHistoryEntry(PaymentHistoryEntry entry) => this.inner.AddHistoryEntry(entry);

    public async Task<PaymentStoreWriteResult> CommitAsync(CancellationToken cancellationToken)
    {
        PaymentStoreWriteResult result = await this.inner.CommitAsync(cancellationToken);
        if (result == PaymentStoreWriteResult.Saved && this.crashSwitch.TryCrash())
        {
            throw new InvalidOperationException("Simulated crash after commit.");
        }

        return result;
    }
}

public sealed class CrashSwitch
{
    private int crashes;

    public int Crashes => this.crashes;

    public bool TryCrash() => Interlocked.CompareExchange(ref this.crashes, 1, 0) == 0;
}
