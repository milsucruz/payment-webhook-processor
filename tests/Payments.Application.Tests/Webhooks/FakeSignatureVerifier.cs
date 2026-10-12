using Payments.Application.Webhooks;

namespace Payments.Application.Tests.Webhooks;

public sealed class FakeSignatureVerifier : IWebhookSignatureVerifier
{
    private readonly SignatureVerificationResult result;

    public FakeSignatureVerifier(SignatureVerificationResult result)
    {
        this.result = result;
    }

    public SignatureVerificationResult Verify(string payload, string? signature) => this.result;
}
