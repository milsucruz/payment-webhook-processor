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
        await DeadLetterAsync(client, "evt_second", "{\"id\":\"evt_second\"}", abandonReason: "database unreachable");
        await DeadLetterAsync(client, "evt_third", "{\"id\":\"evt_third\"}");
        await using ServiceBusDeadLetterQueue deadLetterQueue = new(client, QueueName);

        IReadOnlyList<DeadLetterMessage> before = await deadLetterQueue.PeekAsync(100, CancellationToken.None);
        DeadLetterMessage second = before.Single(message => message.MessageId == "evt_second");
        string? reprocessed = await deadLetterQueue.ReprocessAsync(second.SequenceNumber, CancellationToken.None);
        IReadOnlyList<DeadLetterMessage> after = await deadLetterQueue.PeekAsync(100, CancellationToken.None);

        Assert.Equal(["evt_first", "evt_second", "evt_third"], before.Select(message => message.MessageId));
        Assert.All(before, message => Assert.Equal("InvalidEvent", message.Reason));
        Assert.Equal("The event body is not valid JSON.", second.ErrorDescription);
        Assert.Equal("database unreachable", second.AbandonReason);
        Assert.Equal("evt_second", reprocessed);
        Assert.Equal(["evt_first", "evt_third"], after.Select(message => message.MessageId));

        await using ServiceBusReceiver mainReceiver = client.CreateReceiver(QueueName);
        ServiceBusReceivedMessage? copy = await mainReceiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(copy);
        Assert.Equal("evt_second", copy.MessageId);
        Assert.Equal("{\"id\":\"evt_second\"}", copy.Body.ToString());
        Assert.Equal("application/json", copy.ContentType);
        Assert.Equal(1, copy.DeliveryCount);
        Assert.Null(copy.DeadLetterReason);
        Assert.False(copy.ApplicationProperties.ContainsKey("AbandonReason"));
    }

    [Fact]
    public async Task Unknown_sequence_number_returns_null_and_leaves_the_dead_letter_as_it_was()
    {
        await using ServiceBusClient client = new(this.container.GetConnectionString());
        await DeadLetterAsync(client, "evt_first", "{\"id\":\"evt_first\"}");
        await DeadLetterAsync(client, "evt_second", "{\"id\":\"evt_second\"}");
        await using ServiceBusDeadLetterQueue deadLetterQueue = new(client, QueueName);

        string? reprocessed = await deadLetterQueue.ReprocessAsync(long.MaxValue, CancellationToken.None);
        IReadOnlyList<DeadLetterMessage> after = await deadLetterQueue.PeekAsync(100, CancellationToken.None);

        Assert.Null(reprocessed);
        Assert.Equal(["evt_first", "evt_second"], after.Select(message => message.MessageId));
        await using ServiceBusReceiver mainReceiver = client.CreateReceiver(QueueName);
        Assert.Null(await mainReceiver.ReceiveMessageAsync(TimeSpan.FromSeconds(2)));
    }

    private static async Task DeadLetterAsync(ServiceBusClient client, string messageId, string body, string? abandonReason = null)
    {
        await using ServiceBusSender sender = client.CreateSender(QueueName);
        await sender.SendMessageAsync(new ServiceBusMessage(body) { MessageId = messageId, ContentType = "application/json" });

        await using ServiceBusReceiver receiver = client.CreateReceiver(QueueName);
        ServiceBusReceivedMessage message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10))
            ?? throw new InvalidOperationException($"{messageId} was not received.");
        if (abandonReason is not null)
        {
            await receiver.AbandonMessageAsync(message, new Dictionary<string, object> { ["AbandonReason"] = abandonReason });
            message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10))
                ?? throw new InvalidOperationException($"{messageId} was not received again.");
        }

        await receiver.DeadLetterMessageAsync(message, "InvalidEvent", "The event body is not valid JSON.");
    }
}
