namespace Payments.Application.Reading;

public sealed record GetPaymentResult(GetPaymentOutcome Outcome, PaymentDetail? Payment = null);
