namespace Payments.Infrastructure.Webhooks;

public record SignatureVerificationResult(bool IsValid, string? FailureReason)
{
    public static SignatureVerificationResult Valid() => new(true, null);

    public static SignatureVerificationResult Invalid(string reason) => new(false, reason);
}
