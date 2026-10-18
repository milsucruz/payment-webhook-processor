using Azure.Messaging.ServiceBus;
using Payments.Application.DeadLetters;
using Payments.Infrastructure.Messaging;
using Testcontainers.ServiceBus;

namespace Payments.IntegrationTests.Messaging;

public class ServiceBusDeadLetterQueueTests : IAsyncLifetime
{
    private const string QueueName = "payment-events";

    private readonly ServiceBusContainer container = new ServiceBusBuilder("mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.1")
        .WithAcceptLicenseAgreement(true)
        .WithConfig(Path.Combine(AppContext.BaseDirectory, "servicebus-emulator-config.json"))
        .Build();

    public Task InitializeAsync() => this.container.StartAsync();

    public Task DisposeAsync() => this.container.DisposeAsync().AsTask();

    [Fact]
    public async Task Reprocessed_message_goes_back_to_the_main_queue_and_leaves_the_dead_letter()
    {
        await using ServiceBusClient client = new(this.container.GetConnectionString());
        await DeadLetterAsync(client, "evt_first", "{\"id\":\"evt_first\"}");
        await DeadLetterAsync(client, "evt_second", "{\"id\":\"evt_second\"}");
        await DeadLetterAsync(client, "evt_third", "{\"id\":\"evt_third\"}");
        await using ServiceBusEventPublisher publisher = new(client, QueueName);
        ServiceBusDeadLetterQueue deadLetterQueue = new(client, QueueName, publisher);

        IReadOnlyList<DeadLetter> before = await deadLetterQueue.PeekAsync(100, CancellationToken.None);
        DeadLetter second = Assert.Single(before, deadLetter => deadLetter.EventId == "evt_second");
        string? reprocessed = await deadLetterQueue.ReprocessAsync(second.SequenceNumber, CancellationToken.None);
        IReadOnlyList<DeadLetter> after = await deadLetterQueue.PeekAsync(100, CancellationToken.None);

        Assert.Equal(["evt_first", "evt_second", "evt_third"], before.Select(deadLetter => deadLetter.EventId));
        Assert.All(before, deadLetter => Assert.Equal("InvalidEvent", deadLetter.Reason));
        Assert.Equal("Description of evt_second", second.Description);
        Assert.Equal("evt_second", reprocessed);
        Assert.Equal(["evt_first", "evt_third"], after.Select(deadLetter => deadLetter.EventId));

        await using ServiceBusReceiver mainReceiver = client.CreateReceiver(QueueName);
        ServiceBusReceivedMessage? copy = await mainReceiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(copy);
        Assert.Equal("evt_second", copy.MessageId);
        Assert.Equal("{\"id\":\"evt_second\"}", copy.Body.ToString());
        Assert.Equal("application/json", copy.ContentType);
        Assert.Null(copy.DeadLetterReason);
        Assert.Equal(1, copy.DeliveryCount);
    }

    [Fact]
    public async Task Unknown_sequence_number_returns_null_and_releases_the_other_messages()
    {
        await using ServiceBusClient client = new(this.container.GetConnectionString());
        await DeadLetterAsync(client, "evt_first", "{\"id\":\"evt_first\"}");
        await DeadLetterAsync(client, "evt_second", "{\"id\":\"evt_second\"}");
        await using ServiceBusEventPublisher publisher = new(client, QueueName);
        ServiceBusDeadLetterQueue deadLetterQueue = new(client, QueueName, publisher);
        IReadOnlyList<DeadLetter> before = await deadLetterQueue.PeekAsync(100, CancellationToken.None);

        string? missing = await deadLetterQueue.ReprocessAsync(before.Max(deadLetter => deadLetter.SequenceNumber) + 1000, CancellationToken.None);
        string? first = await deadLetterQueue.ReprocessAsync(before[0].SequenceNumber, CancellationToken.None);

        Assert.Null(missing);
        Assert.Equal("evt_first", first);
        DeadLetter remaining = Assert.Single(await deadLetterQueue.PeekAsync(100, CancellationToken.None));
        Assert.Equal("evt_second", remaining.EventId);
    }

    private static async Task DeadLetterAsync(ServiceBusClient client, string eventId, string body)
    {
        await using ServiceBusSender sender = client.CreateSender(QueueName);
        await sender.SendMessageAsync(new ServiceBusMessage(body) { MessageId = eventId, ContentType = "application/json" });

        await using ServiceBusReceiver receiver = client.CreateReceiver(QueueName);
        ServiceBusReceivedMessage message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10))
            ?? throw new InvalidOperationException($"Message {eventId} was not received.");
        await receiver.DeadLetterMessageAsync(message, "InvalidEvent", $"Description of {eventId}");
    }
}
