namespace Payments.Application.Reading;

public sealed record ListPaymentsResult(ListPaymentsOutcome Outcome, PaymentPage? Page = null);
