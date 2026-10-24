using Microsoft.EntityFrameworkCore;
using Payments.Application.Reading;
using Payments.Domain;

namespace Payments.Infrastructure.Persistence;

// Details: docs/code/PaymentReader.md
public sealed class PaymentReader : IPaymentReader
{
    private readonly PaymentsDbContext dbContext;

    public PaymentReader(PaymentsDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public async Task<IReadOnlyList<PaymentSummary>> ListAsync(PaymentCursor? after, int count, CancellationToken cancellationToken)
    {
        IQueryable<Payment> payments = this.dbContext.Payments.AsNoTracking();
        if (after is not null)
        {
            DateTimeOffset updatedAt = after.UpdatedAt;
            string paymentIntentId = after.PaymentIntentId;
            payments = payments.Where(payment =>
                payment.UpdatedAt < updatedAt
                || (payment.UpdatedAt == updatedAt && string.Compare(payment.PaymentIntentId, paymentIntentId) < 0));
        }

        return await payments
            .OrderByDescending(payment => payment.UpdatedAt)
            .ThenByDescending(payment => payment.PaymentIntentId)
            .Take(count)
            .Select(payment => new PaymentSummary(
                payment.PaymentIntentId,
                payment.Status,
                payment.Amount,
                payment.AmountRefunded,
                payment.Currency,
                payment.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<PaymentDetail?> FindAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        Payment? payment = await this.dbContext.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.PaymentIntentId == paymentIntentId, cancellationToken);
        if (payment is null)
        {
            return null;
        }

        List<PaymentHistoryItem> history = await this.dbContext.PaymentHistory
            .Where(entry => entry.PaymentIntentId == paymentIntentId)
            .Join(
                this.dbContext.InboxEvents,
                entry => entry.EventId,
                inboxEvent => inboxEvent.EventId,
                (entry, inboxEvent) => new { entry, inboxEvent })
            .OrderBy(row => row.entry.Id)
            .Select(row => new PaymentHistoryItem(
                row.entry.EventId,
                row.inboxEvent.EventType,
                row.entry.FromStatus,
                row.entry.ToStatus,
                row.entry.Applied,
                row.entry.Reason,
                row.entry.OccurredAt,
                row.inboxEvent.TraceId))
            .ToListAsync(cancellationToken);

        return new PaymentDetail(
            payment.PaymentIntentId,
            payment.Status,
            payment.Amount,
            payment.AmountRefunded,
            payment.Currency,
            payment.LastEventId,
            payment.CreatedAt,
            payment.UpdatedAt,
            history);
    }
}
