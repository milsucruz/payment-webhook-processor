using Microsoft.Extensions.Options;
using Payments.Application.Webhooks;
using Stripe;

namespace Payments.Infrastructure.Webhooks;

public class StripeWebhookSignatureVerifier : IWebhookSignatureVerifier
{
    public const long ToleranceSeconds = 300;

    private readonly string secret;
    private readonly TimeProvider timeProvider;

    public StripeWebhookSignatureVerifier(IOptions<StripeOptions> options, TimeProvider timeProvider)
    {
        this.secret = options.Value.WebhookSecret;
        this.timeProvider = timeProvider;
    }

    public SignatureVerificationResult Verify(string payload, string? signature)
    {
        if (string.IsNullOrEmpty(signature))
        {
            return SignatureVerificationResult.Invalid("Missing Stripe-Signature header.");
        }

        try
        {
            EventUtility.ValidateSignature(
                payload,
                signature,
                this.secret,
                ToleranceSeconds,
                this.timeProvider.GetUtcNow().ToUnixTimeSeconds());

            return SignatureVerificationResult.Valid();
        }
        catch (StripeException exception)
        {
            return SignatureVerificationResult.Invalid(exception.Message);
        }
    }
}
