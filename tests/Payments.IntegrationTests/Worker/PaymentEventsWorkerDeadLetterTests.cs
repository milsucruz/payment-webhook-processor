using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Payments.Application.Processing;
using Payments.Infrastructure.Persistence;
using Payments.Worker;
using Testcontainers.ServiceBus;

namespace Payments.IntegrationTests.Worker;

public class PaymentEventsWorkerDeadLetterTests : IAsyncLifetime
{
    private const string QueueName = "payment-events";
    private const string UnusedDatabase = "Server=unused;Database=Payments";

    private readonly ServiceBusContainer container = new ServiceBusBuilder("mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.1")
        .WithAcceptLicenseAgreement(true)
        .WithConfig(Path.Combine(AppContext.BaseDirectory, "servicebus-emulator-config.json"))
        .Build();

    public Task InitializeAsync() => this.container.StartAsync();

    public Task DisposeAsync() => this.container.DisposeAsync().AsTask();

    [Fact]
    public async Task Unreadable_event_is_dead_lettered_on_the_first_delivery_with_the_reason()
    {
        await using ServiceBusClient client = new(this.container.GetConnectionString());
        await using ServiceProvider services = BuildServices();
        await using ServiceBusProcessor processor = client.CreateProcessor(QueueName, new ServiceBusProcessorOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            AutoCompleteMessages = false,
        });
        PaymentEventsWorker worker = new(processor, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<PaymentEventsWorker>.Instance);

        await using (ServiceBusSender sender = client.CreateSender(QueueName))
        {
            await sender.SendMessageAsync(new ServiceBusMessage("not json") { MessageId = "evt_unreadable" });
        }

        await worker.StartAsync(CancellationToken.None);
        await using ServiceBusReceiver deadLetterReceiver = client.CreateReceiver(
            QueueName,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
        ServiceBusReceivedMessage? deadLettered = await deadLetterReceiver.ReceiveMessageAsync(TimeSpan.FromSeconds(30));
        await worker.StopAsync(CancellationToken.None);

        Assert.NotNull(deadLettered);
        Assert.Equal("evt_unreadable", deadLettered.MessageId);
        Assert.Equal("not json", deadLettered.Body.ToString());
        Assert.Equal("InvalidEvent", deadLettered.DeadLetterReason);
        Assert.Equal("The event body is not valid JSON.", deadLettered.DeadLetterErrorDescription);
        Assert.Equal(1, deadLettered.DeliveryCount);
    }

    private static ServiceProvider BuildServices()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDbContext<PaymentsDbContext>(options => options.UseSqlServer(UnusedDatabase));
        services.AddScoped<IPaymentStore, PaymentStore>();
        services.AddSingleton<ITransientErrorDetector, SqlTransientErrorDetector>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ProcessStripeEvent>();
        return services.BuildServiceProvider();
    }
}
