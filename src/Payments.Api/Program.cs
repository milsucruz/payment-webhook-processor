using Payments.Infrastructure.Webhooks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Fails at startup without a webhook secret, instead of rejecting every webhook later.
builder.Services.AddOptions<StripeOptions>()
    .BindConfiguration(StripeOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.WebhookSecret), "Stripe:WebhookSecret is required.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IWebhookSignatureVerifier, StripeWebhookSignatureVerifier>();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

var app = builder.Build();

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
