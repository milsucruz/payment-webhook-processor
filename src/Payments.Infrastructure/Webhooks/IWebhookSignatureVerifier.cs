namespace Payments.Infrastructure.Webhooks;

public interface IWebhookSignatureVerifier
{
    SignatureVerificationResult Verify(string payload, string? signatureHeader);
}
