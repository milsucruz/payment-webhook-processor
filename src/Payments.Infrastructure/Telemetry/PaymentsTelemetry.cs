using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Payments.Application.Processing;
using Serilog;
using Serilog.Extensions.Logging;
using Serilog.Formatting.Compact;

namespace Payments.Infrastructure.Telemetry;

// Details: docs/code/PaymentsTelemetry.md
public static class PaymentsTelemetry
{
    public const string ConnectionStringSetting = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    private const string ServiceBusActivitySourceSwitch = "Azure.Experimental.EnableActivitySource";

    public static void Add(IHostApplicationBuilder builder, string serviceName, Action<TracerProviderBuilder>? configureTracing = null)
    {
        AppContext.SetSwitch(ServiceBusActivitySourceSwitch, true);

        Serilog.Core.Logger consoleLogger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console(new RenderedCompactJsonFormatter())
            .CreateLogger();
        builder.Logging.ClearProviders();
        builder.Logging.Configure(options => options.ActivityTrackingOptions = ActivityTrackingOptions.None);
        builder.Logging.AddProvider(new SerilogLoggerProvider(consoleLogger, dispose: true));

        string? connectionString = builder.Configuration[ConnectionStringSetting];
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                configureTracing?.Invoke(tracing);
                tracing
                    .AddSource(ProcessStripeEvent.ActivitySourceName, "Azure.*")
                    .AddHttpClientInstrumentation()
                    .AddSqlClientInstrumentation()
                    .AddProcessor(new ServiceBusPollingFilter());
                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    tracing.AddAzureMonitorTraceExporter(options => ConfigureExporter(options, connectionString));
                }
            })
            .WithLogging(
                logging =>
                {
                    if (!string.IsNullOrWhiteSpace(connectionString))
                    {
                        logging.AddAzureMonitorLogExporter(options => ConfigureExporter(options, connectionString));
                    }
                },
                options =>
                {
                    options.IncludeScopes = true;
                    options.IncludeFormattedMessage = true;
                });
    }

    private static void ConfigureExporter(AzureMonitorExporterOptions options, string connectionString)
    {
        options.ConnectionString = connectionString;
        options.Credential = new DefaultAzureCredential();
        options.SamplingRatio = 1.0F;
        options.TracesPerSecond = null;
    }
}
