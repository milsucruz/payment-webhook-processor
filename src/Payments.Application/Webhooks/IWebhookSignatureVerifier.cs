namespace Payments.Application.Webhooks;

public interface IWebhookSignatureVerifier
{
    SignatureVerificationResult Verify(string payload, string? signature);
}
