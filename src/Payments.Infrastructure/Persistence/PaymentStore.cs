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
        this.dbContext.ChangeTracker.Clear();
        this.transaction = await this.dbContext.Database.BeginTransactionAsync(cancellationToken);
        return this.transaction;
    }

    public async Task<Payment?> FindPaymentAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        return await this.dbContext.Payments.FindAsync([paymentIntentId], cancellationToken);
    }

    public async Task<PaymentStoreWriteResult> AddInboxEventAsync(InboxEvent inboxEvent, CancellationToken cancellationToken)
    {
        this.dbContext.InboxEvents.Add(inboxEvent);
        return await this.SaveAsync(cancellationToken);
    }

    public void AddPayment(Payment payment)
    {
        this.dbContext.Payments.Add(payment);
    }

    public void AddHistoryEntry(PaymentHistoryEntry entry)
    {
        this.dbContext.PaymentHistory.Add(entry);
    }

    public async Task<PaymentStoreWriteResult> CommitAsync(CancellationToken cancellationToken)
    {
        if (this.transaction is null)
        {
            throw new InvalidOperationException("CommitAsync was called without a transaction.");
        }

        PaymentStoreWriteResult result = await this.SaveAsync(cancellationToken);
        if (result == PaymentStoreWriteResult.Saved)
        {
            await this.transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    private async Task<PaymentStoreWriteResult> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await this.dbContext.SaveChangesAsync(cancellationToken);
            return PaymentStoreWriteResult.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            return PaymentStoreWriteResult.ConcurrencyConflict;
        }
        catch (DbUpdateException exception) when (this.ViolatesPrimaryKeyOf<InboxEvent>(exception))
        {
            return PaymentStoreWriteResult.DuplicateEvent;
        }
        catch (DbUpdateException exception) when (this.ViolatesPrimaryKeyOf<Payment>(exception))
        {
            return PaymentStoreWriteResult.ConcurrencyConflict;
        }
    }

    private bool ViolatesPrimaryKeyOf<TEntity>(DbUpdateException exception)
    {
        if (exception.InnerException is not SqlException { Number: PrimaryKeyViolation } sqlException)
        {
            return false;
        }

        string? keyName = this.dbContext.Model.FindEntityType(typeof(TEntity))?.FindPrimaryKey()?.GetName();
        return keyName is not null && sqlException.Message.Contains($"'{keyName}'", StringComparison.Ordinal);
    }
}
