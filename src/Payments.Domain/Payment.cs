namespace Payments.Domain;

public sealed class Payment
{
    public Payment(
        string paymentIntentId,
        PaymentStatus status,
        long amount,
        long amountRefunded,
        string currency,
        string lastEventId,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        this.PaymentIntentId = paymentIntentId;
        this.Status = status;
        this.Amount = amount;
        this.AmountRefunded = amountRefunded;
        this.Currency = currency;
        this.LastEventId = lastEventId;
        this.CreatedAt = createdAt;
        this.UpdatedAt = updatedAt;
    }

    public string PaymentIntentId { get; private set; }

    public PaymentStatus Status { get; private set; }

    public long Amount { get; private set; }

    public long AmountRefunded { get; private set; }

    public string Currency { get; private set; }

    public string LastEventId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
