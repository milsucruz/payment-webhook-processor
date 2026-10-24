namespace Payments.Application.Reading;

public interface IPaymentReader
{
    Task<IReadOnlyList<PaymentSummary>> ListAsync(PaymentCursor? after, int count, CancellationToken cancellationToken);

    Task<PaymentDetail?> FindAsync(string paymentIntentId, CancellationToken cancellationToken);
}
