namespace Payments.Application.Reading;

public sealed record PaymentPage(IReadOnlyList<PaymentSummary> Items, string? NextCursor);
