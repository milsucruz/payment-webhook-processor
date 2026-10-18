using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payments.Application.Processing;
using Payments.Infrastructure.Faults;
using Payments.Infrastructure.Messaging;
using Payments.Infrastructure.Persistence;
using Payments.Worker;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

Program.AddPaymentEventsWorker(builder.Services, builder.Configuration);

IHost host = builder.Build();
host.Run();

public partial class Program
{
    public static void AddPaymentEventsWorker(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ServiceBusOptions>()
            .BindConfiguration(ServiceBusOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "ServiceBus:ConnectionString is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.EventsQueueName), "ServiceBus:EventsQueueName is required.")
            .ValidateOnStart();
        services.AddSingleton(serviceProvider =>
            new ServiceBusClient(serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value.ConnectionString));
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

        services.AddDbContext<PaymentsDbContext>(options => options.UseSqlServer(paymentsConnectionString));
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

        services.AddScoped<ProcessStripeEvent>();

        services.AddHostedService<PaymentEventsWorker>();
    }
}
