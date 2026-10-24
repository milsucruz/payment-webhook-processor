using Microsoft.Extensions.Logging;

namespace Payments.Application.Reading;

public sealed class GetPayment
{
    private readonly IPaymentReader paymentReader;
    private readonly ILogger<GetPayment> logger;

    public GetPayment(IPaymentReader paymentReader, ILogger<GetPayment> logger)
    {
        this.paymentReader = paymentReader;
        this.logger = logger;
    }

    public async Task<GetPaymentResult> ExecuteAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        PaymentDetail? payment = await this.paymentReader.FindAsync(paymentIntentId, cancellationToken);
        if (payment is null)
        {
            this.logger.LogInformation("No payment {PaymentIntentId}", paymentIntentId);
            return new GetPaymentResult(GetPaymentOutcome.NotFound);
        }

        return new GetPaymentResult(GetPaymentOutcome.Found, payment);
    }
}
