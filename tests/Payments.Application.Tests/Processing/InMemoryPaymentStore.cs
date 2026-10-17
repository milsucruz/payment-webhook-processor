using Payments.Application.Processing;
using Payments.Domain;

namespace Payments.Application.Tests.Processing;

public sealed class InMemoryPaymentStore : IPaymentStore
{
    private readonly List<InboxEvent> pendingInboxEvents = [];
    private readonly List<Payment> pendingPayments = [];
    private readonly List<PaymentHistoryEntry> pendingHistory = [];

    public Dictionary<string, Payment> Payments { get; } = [];

    public List<InboxEvent> InboxEvents { get; } = [];

    public List<PaymentHistoryEntry> History { get; } = [];

    public int TransactionsStarted { get; private set; }

    public int Commits { get; private set; }

    public Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        this.TransactionsStarted++;
        return Task.FromResult<IAsyncDisposable>(new Transaction(this));
    }

    public Task<Payment?> FindPaymentAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        return Task.FromResult(this.Payments.GetValueOrDefault(paymentIntentId));
    }

    public Task<bool> TryAddInboxEventAsync(InboxEvent inboxEvent, CancellationToken cancellationToken)
    {
        if (this.InboxEvents.Any(existing => existing.EventId == inboxEvent.EventId))
        {
            return Task.FromResult(false);
        }

        this.pendingInboxEvents.Add(inboxEvent);
        return Task.FromResult(true);
    }

    public void AddPayment(Payment payment)
    {
        this.pendingPayments.Add(payment);
    }

    public void AddHistoryEntry(PaymentHistoryEntry entry)
    {
        this.pendingHistory.Add(entry);
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        this.InboxEvents.AddRange(this.pendingInboxEvents);
        foreach (Payment payment in this.pendingPayments)
        {
            this.Payments.Add(payment.PaymentIntentId, payment);
        }

        this.History.AddRange(this.pendingHistory);
        this.Commits++;
        this.Discard();
        return Task.CompletedTask;
    }

    private void Discard()
    {
        this.pendingInboxEvents.Clear();
        this.pendingPayments.Clear();
        this.pendingHistory.Clear();
    }

    private sealed class Transaction : IAsyncDisposable
    {
        private readonly InMemoryPaymentStore store;

        public Transaction(InMemoryPaymentStore store)
        {
            this.store = store;
        }

        public ValueTask DisposeAsync()
        {
            this.store.Discard();
            return ValueTask.CompletedTask;
        }
    }
}
