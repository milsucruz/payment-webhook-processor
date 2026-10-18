namespace Payments.Api.RateLimiting;

public class WebhookRateLimitOptions
{
    public const string SectionName = "WebhookRateLimit";

    public const string PolicyName = "webhook";

    public int PermitLimit { get; set; }

    public TimeSpan Window { get; set; }
}
