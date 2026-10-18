using Microsoft.EntityFrameworkCore.Storage;
using Payments.Application.Processing;
using Payments.Domain;
using Payments.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace Payments.IntegrationTests.Processing;

public sealed class PausingPaymentStore : IPaymentStore
{
    private readonly PaymentStore inner;
    private readonly PaymentsDbContext dbContext;
    private readonly MsSqlContainer container;
    private readonly bool rollBackOnDispose;

    public PausingPaymentStore(PaymentsDbContext dbContext, MsSqlContainer container, bool rollBackOnDispose)
    {
        this.inner = new PaymentStore(dbContext);
        this.dbContext = dbContext;
        this.container = container;
        this.rollBackOnDispose = rollBackOnDispose;
    }

    public int Transactions { get; private set; }

    public int RollbackFailuresWithTransactionLeftOpen { get; private set; }

    public async Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        this.Transactions++;
        if (this.Transactions == 2)
        {
            await this.container.UnpauseAsync(cancellationToken);
        }

        IAsyncDisposable transaction = await this.inner.BeginTransactionAsync(cancellationToken);
        return this.rollBackOnDispose && this.Transactions == 1
            ? new RollBackOnDisposeTransaction((IDbContextTransaction)transaction, this)
            : transaction;
    }

    public async Task<Payment?> FindPaymentAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        Payment? payment = await this.inner.FindPaymentAsync(paymentIntentId, cancellationToken);
        if (this.Transactions == 1)
        {
            await this.container.PauseAsync(cancellationToken);
        }

        return payment;
    }

    public Task<PaymentStoreWriteResult> AddInboxEventAsync(InboxEvent inboxEvent, CancellationToken cancellationToken)
    {
        return this.inner.AddInboxEventAsync(inboxEvent, cancellationToken);
    }

    public void AddPayment(Payment payment)
    {
        this.inner.AddPayment(payment);
    }

    public void AddHistoryEntry(PaymentHistoryEntry entry)
    {
        this.inner.AddHistoryEntry(entry);
    }

    public Task<PaymentStoreWriteResult> CommitAsync(CancellationToken cancellationToken)
    {
        return this.inner.CommitAsync(cancellationToken);
    }

    private sealed class RollBackOnDisposeTransaction : IAsyncDisposable
    {
        private readonly IDbContextTransaction transaction;
        private readonly PausingPaymentStore store;

        public RollBackOnDisposeTransaction(IDbContextTransaction transaction, PausingPaymentStore store)
        {
            this.transaction = transaction;
            this.store = store;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await this.transaction.RollbackAsync();
            }
            catch (Exception)
            {
                if (this.store.dbContext.Database.CurrentTransaction is not null)
                {
                    this.store.RollbackFailuresWithTransactionLeftOpen++;
                }

                throw;
            }
        }
    }
}
