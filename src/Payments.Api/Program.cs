using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Payments.Api.Authorization;
using Payments.Api.Hubs;
using Payments.Api.Notifications;
using Payments.Api.Queues;
using Payments.Api.RateLimiting;
using Payments.Application.DeadLetters;
using Payments.Application.Messaging;
using Payments.Application.Queues;
using Payments.Application.Reading;
using Payments.Application.Webhooks;
using Payments.Infrastructure.Messaging;
using Payments.Infrastructure.Persistence;
using Payments.Infrastructure.Webhooks;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOptions<StripeOptions>()
    .BindConfiguration(StripeOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.WebhookSecret), "Stripe:WebhookSecret is required.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IWebhookSignatureVerifier, StripeWebhookSignatureVerifier>();

builder.Services.AddOptions<ServiceBusOptions>()
    .BindConfiguration(ServiceBusOptions.SectionName)
    .Validate(options => options.HasSingleEndpoint, "Set exactly one of ServiceBus:ConnectionString and ServiceBus:FullyQualifiedNamespace.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.EventsQueueName), "ServiceBus:EventsQueueName is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.NotificationsQueueName), "ServiceBus:NotificationsQueueName is required.")
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

    return ServiceBusClientFactory.Create(options, clientOptions);
});
builder.Services.AddSingleton<IEventPublisher>(serviceProvider => new ServiceBusEventPublisher(
    serviceProvider.GetRequiredService<ServiceBusClient>(),
    serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value.EventsQueueName));

builder.Services.AddSingleton<IDeadLetterQueue>(serviceProvider => new ServiceBusDeadLetterQueue(
    serviceProvider.GetRequiredService<ServiceBusClient>(),
    serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value.EventsQueueName,
    serviceProvider.GetRequiredService<IEventPublisher>()));

builder.Services.AddKeyedSingleton(PaymentNotificationsListener.ServiceKey, (serviceProvider, _) =>
    ServiceBusClientFactory.Create(serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value));
builder.Services.AddKeyedSingleton(PaymentNotificationsListener.ServiceKey, (serviceProvider, key) =>
{
    ServiceBusProcessorOptions processorOptions = new()
    {
        ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete,
        MaxConcurrentCalls = 1,
    };

    return serviceProvider.GetRequiredKeyedService<ServiceBusClient>(key).CreateProcessor(
        serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value.NotificationsQueueName,
        processorOptions);
});
builder.Services.AddHostedService<PaymentNotificationsListener>();

builder.Services.AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<HubConnectionCounter>();

builder.Services.AddSingleton<IQueueStatsReader>(serviceProvider => new ServiceBusQueueStatsReader(
    serviceProvider.GetRequiredService<ServiceBusClient>(),
    serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value.EventsQueueName));
builder.Services.AddSingleton<GetQueueStats>();
builder.Services.AddOptions<QueueStatsOptions>()
    .BindConfiguration(QueueStatsOptions.SectionName)
    .Validate(options => options.Interval > TimeSpan.Zero, "QueueStats:Interval must be positive.")
    .ValidateOnStart();
builder.Services.AddHostedService<QueueStatsBroadcaster>();

string? paymentsConnectionString = builder.Configuration.GetConnectionString("Payments");
if (string.IsNullOrWhiteSpace(paymentsConnectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Payments is required.");
}

builder.Services.AddDbContext<PaymentsDbContext>(options =>
    options.UseSqlServer(paymentsConnectionString, sqlServer => sqlServer.EnableRetryOnFailure()));
builder.Services.AddScoped<IPaymentReader, PaymentReader>();

builder.Services.AddSingleton<ReceiveStripeEvent>();
builder.Services.AddSingleton<ListDeadLetters>();
builder.Services.AddSingleton<ReprocessDeadLetter>();
builder.Services.AddScoped<ListPayments>();
builder.Services.AddScoped<GetPayment>();

builder.Services.AddOptions<AdminOptions>()
    .BindConfiguration(AdminOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "Admin:ApiKey is required.")
    .ValidateOnStart();

builder.Services.AddOptions<WebhookRateLimitOptions>()
    .BindConfiguration(WebhookRateLimitOptions.SectionName)
    .Validate(options => options.PermitLimit > 0, "WebhookRateLimit:PermitLimit must be positive.")
    .Validate(options => options.Window > TimeSpan.Zero, "WebhookRateLimit:Window must be positive.")
    .ValidateOnStart();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
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

builder.Services.AddHealthChecks()
    .Add(new HealthCheckRegistration(
        "service-bus",
        serviceProvider => new ServiceBusQueueHealthCheck(
            serviceProvider.GetRequiredService<ServiceBusClient>(),
            serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value.EventsQueueName),
        failureStatus: null,
        tags: null));
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

// Development only: the webhook endpoint is public in the cloud, so the API description is not exposed there.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Payments API"));
}

app.UseForwardedHeaders();
app.UseDefaultFiles();
app.UseRouting();
app.UseRateLimiter();

app.MapStaticAssets();
app.MapControllers();
app.MapHub<PaymentsHub>(PaymentsHub.Route);

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

app.Run();
