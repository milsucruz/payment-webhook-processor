using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Payments.Application.Processing;
using Payments.Domain;

namespace Payments.Infrastructure.Persistence;

// Details: docs/code/PaymentStore.md
public sealed class PaymentStore : IPaymentStore
{
    private const int PrimaryKeyViolation = 2627;

    private readonly PaymentsDbContext dbContext;
    private IDbContextTransaction? transaction;

    public PaymentStore(PaymentsDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public async Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        this.transaction = await this.dbContext.Database.BeginTransactionAsync(cancellationToken);
        return this.transaction;
    }

    public async Task<Payment?> FindPaymentAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        return await this.dbContext.Payments.FindAsync([paymentIntentId], cancellationToken);
    }

    public async Task<bool> TryAddInboxEventAsync(InboxEvent inboxEvent, CancellationToken cancellationToken)
    {
        this.dbContext.InboxEvents.Add(inboxEvent);
        try
        {
            await this.dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: PrimaryKeyViolation })
        {
            return false;
        }
    }

    public void AddPayment(Payment payment)
    {
        this.dbContext.Payments.Add(payment);
    }

    public void AddHistoryEntry(PaymentHistoryEntry entry)
    {
        this.dbContext.PaymentHistory.Add(entry);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (this.transaction is null)
        {
            throw new InvalidOperationException("CommitAsync was called without a transaction.");
        }

        await this.dbContext.SaveChangesAsync(cancellationToken);
        await this.transaction.CommitAsync(cancellationToken);
    }
}
