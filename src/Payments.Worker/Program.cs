using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payments.Application.Processing;
using Payments.Infrastructure.Messaging;
using Payments.Infrastructure.Persistence;
using Payments.Worker;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<ServiceBusOptions>()
    .BindConfiguration(ServiceBusOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "ServiceBus:ConnectionString is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.EventsQueueName), "ServiceBus:EventsQueueName is required.")
    .ValidateOnStart();
builder.Services.AddSingleton(serviceProvider =>
    new ServiceBusClient(serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value.ConnectionString));
builder.Services.AddSingleton(serviceProvider =>
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

string? paymentsConnectionString = builder.Configuration.GetConnectionString("Payments");
if (string.IsNullOrWhiteSpace(paymentsConnectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Payments is required.");
}

builder.Services.AddDbContext<PaymentsDbContext>(options => options.UseSqlServer(paymentsConnectionString));
builder.Services.AddScoped<IPaymentStore, PaymentStore>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ProcessStripeEvent>();

builder.Services.AddHostedService<PaymentEventsWorker>();

IHost host = builder.Build();
host.Run();
