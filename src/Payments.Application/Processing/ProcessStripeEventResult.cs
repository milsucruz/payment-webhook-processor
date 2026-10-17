namespace Payments.Application.Processing;

public sealed record ProcessStripeEventResult(ProcessStripeEventOutcome Outcome, string? Reason = null);
