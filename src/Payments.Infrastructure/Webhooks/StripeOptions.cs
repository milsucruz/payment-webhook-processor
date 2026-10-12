namespace Payments.Infrastructure.Webhooks;

public class StripeOptions
{
    public const string SectionName = "Stripe";

    public string WebhookSecret { get; set; } = string.Empty;
}
