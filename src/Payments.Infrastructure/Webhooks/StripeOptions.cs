namespace Payments.Infrastructure.Webhooks;

// Bound from the "Stripe" configuration section; locally from user-secrets or .env, in the cloud from Key Vault.
public class StripeOptions
{
    public const string SectionName = "Stripe";

    public string WebhookSecret { get; set; } = string.Empty;
}
