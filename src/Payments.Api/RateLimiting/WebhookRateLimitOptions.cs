namespace Payments.Api.RateLimiting;

public class WebhookRateLimitOptions
{
    public const string SectionName = "RateLimiting:Webhook";
    public const string PolicyName = "webhook";

    public int PermitLimit { get; set; } = 100;

    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(1);
}
