using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Payments.Infrastructure.Messaging;
using Testcontainers.ServiceBus;

namespace Payments.IntegrationTests.Messaging;

public class ServiceBusQueueHealthCheckTests : IAsyncLifetime
{
    private const string QueueName = "payment-events";

    private readonly ServiceBusContainer container = new ServiceBusBuilder("mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.1")
        .WithAcceptLicenseAgreement(true)
        .WithConfig(Path.Combine(AppContext.BaseDirectory, "servicebus-emulator-config.json"))
        .Build();

    public Task InitializeAsync() => this.container.StartAsync();

    public Task DisposeAsync() => this.container.DisposeAsync().AsTask();

    [Fact]
    public async Task Healthy_when_the_queue_is_reachable_and_the_peeked_message_stays_in_the_queue()
    {
        await using ServiceBusClient client = new(this.container.GetConnectionString());
        await using ServiceBusSender sender = client.CreateSender(QueueName);
        await sender.SendMessageAsync(new ServiceBusMessage("{}") { MessageId = "evt_health" });
        ServiceBusQueueHealthCheck check = new(client, QueueName);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        await using ServiceBusReceiver receiver = client.CreateReceiver(QueueName);
        ServiceBusReceivedMessage? message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(message);
        Assert.Equal("evt_health", message.MessageId);
        Assert.Equal(1, message.DeliveryCount);
    }
}
