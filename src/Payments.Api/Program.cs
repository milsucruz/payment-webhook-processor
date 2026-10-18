using System.Threading.RateLimiting;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Payments.Api.Authorization;
using Payments.Api.RateLimiting;
using Payments.Application.DeadLetters;
using Payments.Application.Messaging;
using Payments.Application.Webhooks;
using Payments.Infrastructure.Messaging;
using Payments.Infrastructure.Webhooks;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddOptions<StripeOptions>()
    .BindConfiguration(StripeOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.WebhookSecret), "Stripe:WebhookSecret is required.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IWebhookSignatureVerifier, StripeWebhookSignatureVerifier>();

builder.Services.AddOptions<ServiceBusOptions>()
    .BindConfiguration(ServiceBusOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "ServiceBus:ConnectionString is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.EventsQueueName), "ServiceBus:EventsQueueName is required.")
    .ValidateOnStart();
builder.Services.AddSingleton(serviceProvider =>
{
    ServiceBusOptions options = serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value;

    ServiceBusClientOptions clientOptions = new()
    {
        RetryOptions = new ServiceBusRetryOptions
        {
            Mode = ServiceBusRetryMode.Exponential,
            MaxRetries = 2,
            Delay = TimeSpan.FromMilliseconds(500),
            MaxDelay = TimeSpan.FromSeconds(2),
            TryTimeout = TimeSpan.FromSeconds(2),
        },
    };

    return new ServiceBusClient(options.ConnectionString, clientOptions);
});
builder.Services.AddSingleton<IEventPublisher>(serviceProvider => new ServiceBusEventPublisher(
    serviceProvider.GetRequiredService<ServiceBusClient>(),
    serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value.EventsQueueName));

builder.Services.AddSingleton<IDeadLetterQueue>(serviceProvider => new ServiceBusDeadLetterQueue(
    serviceProvider.GetRequiredService<ServiceBusClient>(),
    serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value.EventsQueueName,
    serviceProvider.GetRequiredService<IEventPublisher>()));

builder.Services.AddSingleton<ReceiveStripeEvent>();
builder.Services.AddSingleton<ListDeadLetters>();
builder.Services.AddSingleton<ReprocessDeadLetter>();

builder.Services.AddOptions<AdminOptions>()
    .BindConfiguration(AdminOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "Admin:ApiKey is required.")
    .ValidateOnStart();

builder.Services.AddOptions<WebhookRateLimitOptions>()
    .BindConfiguration(WebhookRateLimitOptions.SectionName)
    .Validate(options => options.PermitLimit > 0, "WebhookRateLimit:PermitLimit must be positive.")
    .Validate(options => options.Window > TimeSpan.Zero, "WebhookRateLimit:Window must be positive.")
    .ValidateOnStart();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(WebhookRateLimitOptions.PolicyName, httpContext =>
    {
        WebhookRateLimitOptions limit = httpContext.RequestServices.GetRequiredService<IOptions<WebhookRateLimitOptions>>().Value;
        string clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit.PermitLimit,
            Window = limit.Window,
            QueueLimit = 0,
        });
    });
});

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

// Development only: the webhook endpoint is public in the cloud, so the API description is not exposed there.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Payments API"));
}

app.UseRouting();
app.UseRateLimiter();

app.MapControllers();

// Liveness only: no checks registered, so it answers as long as the process responds (architecture section 9).
app.MapHealthChecks("/health/live");

app.Run();
