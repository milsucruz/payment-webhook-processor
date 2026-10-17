namespace Payments.Domain.Tests;

public class PaymentTests
{
    private static readonly DateTimeOffset createdAt = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset laterAt = createdAt.AddMinutes(5);

    [Fact]
    public void Applied_event_changes_status_refunded_amount_last_event_and_updated_at()
    {
        Payment payment = CreatePayment(PaymentStatus.Paid, amountRefunded: 0);

        TransitionOutcome outcome = payment.Apply(PaymentStatus.PartiallyRefunded, 500, "evt_refund", laterAt);

        Assert.Equal(TransitionOutcome.Applied, outcome);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(500, payment.AmountRefunded);
        Assert.Equal("evt_refund", payment.LastEventId);
        Assert.Equal(laterAt, payment.UpdatedAt);
    }

    [Fact]
    public void Applied_event_keeps_amount_currency_and_created_at()
    {
        Payment payment = CreatePayment(PaymentStatus.Created, amountRefunded: 0);

        payment.Apply(PaymentStatus.Paid, 0, "evt_paid", laterAt);

        Assert.Equal(2_000, payment.Amount);
        Assert.Equal("brl", payment.Currency);
        Assert.Equal(createdAt, payment.CreatedAt);
    }

    [Theory]
    [InlineData(PaymentStatus.Paid, 0, PaymentStatus.Created, 0, TransitionOutcome.IgnoredOutOfOrder)]
    [InlineData(PaymentStatus.Paid, 0, PaymentStatus.Paid, 0, TransitionOutcome.Redundant)]
    [InlineData(PaymentStatus.PartiallyRefunded, 500, PaymentStatus.PartiallyRefunded, 300, TransitionOutcome.IgnoredOutOfOrder)]
    [InlineData(PaymentStatus.Canceled, 0, PaymentStatus.Paid, 0, TransitionOutcome.Conflict)]
    public void Event_that_is_not_applied_leaves_the_payment_unchanged(
        PaymentStatus current,
        long currentAmountRefunded,
        PaymentStatus requested,
        long requestedAmountRefunded,
        TransitionOutcome expected)
    {
        Payment payment = CreatePayment(current, currentAmountRefunded);

        TransitionOutcome outcome = payment.Apply(requested, requestedAmountRefunded, "evt_other", laterAt);

        Assert.Equal(expected, outcome);
        Assert.Equal(current, payment.Status);
        Assert.Equal(currentAmountRefunded, payment.AmountRefunded);
        Assert.Equal("evt_first", payment.LastEventId);
        Assert.Equal(createdAt, payment.UpdatedAt);
    }

    private static Payment CreatePayment(PaymentStatus status, long amountRefunded)
    {
        return new Payment("pi_test", status, 2_000, amountRefunded, "brl", "evt_first", createdAt, createdAt);
    }
}
