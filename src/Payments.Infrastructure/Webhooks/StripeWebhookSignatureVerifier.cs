using Microsoft.Extensions.Options;
using Stripe;

namespace Payments.Infrastructure.Webhooks;

// Details: docs/code/StripeWebhookSignatureVerifier.md
public class StripeWebhookSignatureVerifier : IWebhookSignatureVerifier
{
    // Architecture section 8: 5-minute tolerance against replay.
    public const long ToleranceSeconds = 300;

    private readonly string secret;
    private readonly TimeProvider timeProvider;

    public StripeWebhookSignatureVerifier(IOptions<StripeOptions> options, TimeProvider timeProvider)
    {
        this.secret = options.Value.WebhookSecret;
        this.timeProvider = timeProvider;
    }

    public SignatureVerificationResult Verify(string payload, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(signatureHeader))
        {
            return SignatureVerificationResult.Invalid("Missing Stripe-Signature header.");
        }

        try
        {
            // Signature only, no parsing: the event stays raw JSON until the worker reads it.
            EventUtility.ValidateSignature(
                payload,
                signatureHeader,
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
