using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Payments.Application.Notifications;
using Payments.Application.Processing;
using Payments.Application.Telemetry;
using Payments.Infrastructure.Faults;
using Payments.Infrastructure.Messaging;
using Payments.Infrastructure.Persistence;
using Payments.Infrastructure.Telemetry;
using Payments.Worker;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

PaymentsTelemetry.Add(builder, "payments-worker");

Program.AddPaymentEventsWorker(builder.Services, builder.Configuration);

IHost host = builder.Build();
host.Run();

public partial class Program
{
    private const string NotificationsClientKey = "payment-notifications";

    public static void AddPaymentEventsWorker(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ServiceBusOptions>()
            .BindConfiguration(ServiceBusOptions.SectionName)
            .Validate(options => options.HasSingleEndpoint, "Set exactly one of ServiceBus:ConnectionString and ServiceBus:FullyQualifiedNamespace.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.EventsQueueName), "ServiceBus:EventsQueueName is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.NotificationsQueueName), "ServiceBus:NotificationsQueueName is required.")
            .ValidateOnStart();
        services.AddSingleton(serviceProvider =>
            ServiceBusClientFactory.Create(serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value));
        services.AddKeyedSingleton(NotificationsClientKey, (serviceProvider, _) =>
        {
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

            return ServiceBusClientFactory.Create(serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value, clientOptions);
        });
        services.AddSingleton<IPaymentNotificationPublisher>(serviceProvider => new ServiceBusPaymentNotificationPublisher(
            serviceProvider.GetRequiredKeyedService<ServiceBusClient>(NotificationsClientKey),
            serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value.NotificationsQueueName));
        services.AddSingleton(serviceProvider =>
        {
            ServiceBusProcessorOptions processorOptions = new()
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                AutoCompleteMessages = false,
                MaxConcurrentCalls = 4,
                PrefetchCount = 0,
            };

            return serviceProvider.GetRequiredService<ServiceBusClient>().CreateProcessor(
                serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value.EventsQueueName,
                processorOptions);
        });

        string? paymentsConnectionString = configuration.GetConnectionString("Payments");
        if (string.IsNullOrWhiteSpace(paymentsConnectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Payments is required.");
        }

        services.AddDbContext<PaymentsDbContext>(options => options
            .UseSqlServer(paymentsConnectionString)
            .ConfigureWarnings(warnings => warnings.Log(
                (RelationalEventId.CommandError, LogLevel.Debug),
                (CoreEventId.SaveChangesFailed, LogLevel.Debug))));
        services.AddScoped<IPaymentStore, PaymentStore>();
        services.AddSingleton<ITransientErrorDetector, SqlTransientErrorDetector>();
        services.AddSingleton(TimeProvider.System);
        if (configuration.GetValue<bool>("FaultInjection:Enabled"))
        {
            services.AddSingleton<IFaultInjector, DemoFaultInjector>();
        }
        else
        {
            services.AddSingleton<IFaultInjector, NoFaultInjector>();
        }

        services.AddMetrics();
        services.AddSingleton<PaymentsMetrics>();
        services.AddScoped<ProcessStripeEvent>();

        services.AddHostedService<PaymentEventsWorker>();
    }
}
