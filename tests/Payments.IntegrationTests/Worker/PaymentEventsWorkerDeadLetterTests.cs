using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Payments.Application.Processing;
using Payments.Infrastructure.Faults;
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

    [Fact]
    public async Task Transient_failure_is_abandoned_with_the_reason_as_an_application_property()
    {
        await using ServiceBusClient client = new(this.container.GetConnectionString());
        await using ServiceProvider services = BuildServices(new AlwaysTransientFaultInjector());
        await using ServiceBusProcessor processor = client.CreateProcessor(QueueName, new ServiceBusProcessorOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            AutoCompleteMessages = false,
        });
        PaymentEventsWorker worker = new(processor, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<PaymentEventsWorker>.Instance);
        string body = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "payment_intent.succeeded.json"));

        await using (ServiceBusSender sender = client.CreateSender(QueueName))
        {
            await sender.SendMessageAsync(new ServiceBusMessage(body) { MessageId = "evt_transient" });
        }

        await worker.StartAsync(CancellationToken.None);
        await using ServiceBusReceiver peeker = client.CreateReceiver(QueueName);
        object? abandonReason = null;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        while (abandonReason is null && !timeout.IsCancellationRequested)
        {
            ServiceBusReceivedMessage? peeked = await peeker.PeekMessageAsync(fromSequenceNumber: 0);
            peeked?.ApplicationProperties.TryGetValue("AbandonReason", out abandonReason);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        await worker.StopAsync(CancellationToken.None);

        string reason = Assert.IsType<string>(abandonReason);
        Assert.StartsWith("Delivery 1: Event evt_", reason, StringComparison.Ordinal);
        Assert.Contains("failed with a transient error on all 4 attempts", reason, StringComparison.Ordinal);
        Assert.Contains("Simulated database timeout.", reason, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildServices(IFaultInjector? faultInjector = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDbContext<PaymentsDbContext>(options => options.UseSqlServer(UnusedDatabase));
        services.AddScoped<IPaymentStore, PaymentStore>();
        services.AddSingleton<ITransientErrorDetector, SqlTransientErrorDetector>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IFaultInjector>(faultInjector ?? new NoFaultInjector());
        services.AddScoped<ProcessStripeEvent>();
        return services.BuildServiceProvider();
    }
}
