using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Payments.Api.Authorization;
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
    serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value.EventsQueueName));

builder.Services.AddSingleton<ReceiveStripeEvent>();
builder.Services.AddSingleton<ListDeadLetters>();
builder.Services.AddSingleton<ReprocessDeadLetter>();

builder.Services.AddOptions<AdminOptions>()
    .BindConfiguration(AdminOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "Admin:ApiKey is required.")
    .ValidateOnStart();

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

// Development only: the webhook endpoint is public in the cloud, so the API description is not exposed there.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Payments API"));
}

app.MapControllers();

// Liveness only: no checks registered, so it answers as long as the process responds (architecture section 9).
app.MapHealthChecks("/health/live");

app.Run();
