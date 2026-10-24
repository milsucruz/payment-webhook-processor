using Azure.Messaging.ServiceBus;
using Payments.Application.Queues;
using Payments.Infrastructure.Messaging;
using Payments.IntegrationTests.Worker;

namespace Payments.IntegrationTests.Messaging;

[Collection(WorkerCollection.Name)]
public class ServiceBusQueueStatsReaderTests : IAsyncLifetime
{
    private readonly WorkerFixture fixture;

    public ServiceBusQueueStatsReaderTests(WorkerFixture fixture)
    {
        this.fixture = fixture;
    }

    public Task InitializeAsync() => this.fixture.PurgeAsync(CancellationToken.None);

    public Task DisposeAsync() => this.fixture.PurgeAsync(CancellationToken.None);

    [Fact]
    public async Task Counts_active_and_dead_lettered_messages()
    {
        await using ServiceBusClient client = new(this.fixture.ServiceBusConnectionString);
        for (int index = 0; index < 4; index++)
        {
            await this.fixture.SendAsync($"evt_stats_{index}", "{}", CancellationToken.None);
        }

        await using ServiceBusReceiver receiver = client.CreateReceiver(WorkerFixture.QueueName);
        ServiceBusReceivedMessage? message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(message);
        await receiver.DeadLetterMessageAsync(message, "InvalidEvent", "Stats test");
        ServiceBusQueueStatsReader reader = new(client, WorkerFixture.QueueName);

        QueueStats stats = await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(new QueueStats(3, 1, ServiceBusQueueStatsReader.MaxCountedMessages), stats);
    }

    [Fact]
    public async Task Empty_queue_counts_zero()
    {
        await using ServiceBusClient client = new(this.fixture.ServiceBusConnectionString);
        ServiceBusQueueStatsReader reader = new(client, WorkerFixture.QueueName);

        QueueStats stats = await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(new QueueStats(0, 0, ServiceBusQueueStatsReader.MaxCountedMessages), stats);
    }
}
