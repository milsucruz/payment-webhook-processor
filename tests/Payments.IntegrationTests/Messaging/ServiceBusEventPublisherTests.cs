using System.Net.Mime;
using Azure.Messaging.ServiceBus;
using Payments.Infrastructure.Messaging;
using Testcontainers.ServiceBus;

namespace Payments.IntegrationTests.Messaging;

public class ServiceBusEventPublisherTests : IAsyncLifetime
{
    private const string QueueName = "payment-events";

    private readonly ServiceBusContainer container = new ServiceBusBuilder("mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.1")
        .WithAcceptLicenseAgreement(true)
        .WithConfig(Path.Combine(AppContext.BaseDirectory, "servicebus-emulator-config.json"))
        .Build();

    public Task InitializeAsync() => this.container.StartAsync();

    public Task DisposeAsync() => this.container.DisposeAsync().AsTask();

    [Fact]
    public async Task Published_event_is_received_with_the_event_id_as_message_id_and_the_original_json_as_body()
    {
        const string EventId = "evt_test_1";
        const string Json = """{"id":"evt_test_1","type":"payment_intent.succeeded","data":{"object":{"id":"pi_test_1","amount":2000}}}""";

        await using var client = new ServiceBusClient(this.container.GetConnectionString());
        await using (var publisher = new ServiceBusEventPublisher(client, QueueName))
        {
            await publisher.PublishAsync(EventId, Json, CancellationToken.None);
        }

        await using var receiver = client.CreateReceiver(QueueName);
        var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(message);
        Assert.Equal(EventId, message.MessageId);
        Assert.Equal(Json, message.Body.ToString());
        Assert.Equal(MediaTypeNames.Application.Json, message.ContentType);
    }
}
