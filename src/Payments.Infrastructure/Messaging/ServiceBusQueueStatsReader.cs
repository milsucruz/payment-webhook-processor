using Azure.Messaging.ServiceBus;
using Payments.Application.Queues;

namespace Payments.Infrastructure.Messaging;

// Details: docs/code/ServiceBusQueueStatsReader.md
public sealed class ServiceBusQueueStatsReader : IQueueStatsReader
{
    public const int MaxCountedMessages = 1000;

    private const int PeekBatchSize = 250;

    private readonly ServiceBusClient client;
    private readonly string queueName;

    public ServiceBusQueueStatsReader(ServiceBusClient client, string queueName)
    {
        this.client = client;
        this.queueName = queueName;
    }

    public async Task<QueueStats> ReadAsync(CancellationToken cancellationToken)
    {
        int activeMessages = await this.CountAsync(SubQueue.None, cancellationToken);
        int deadLetteredMessages = await this.CountAsync(SubQueue.DeadLetter, cancellationToken);
        return new QueueStats(activeMessages, deadLetteredMessages);
    }

    private async Task<int> CountAsync(SubQueue subQueue, CancellationToken cancellationToken)
    {
        await using ServiceBusReceiver receiver = this.client.CreateReceiver(
            this.queueName,
            new ServiceBusReceiverOptions { SubQueue = subQueue });
        int count = 0;

        while (count < MaxCountedMessages)
        {
            IReadOnlyList<ServiceBusReceivedMessage> batch = await receiver.PeekMessagesAsync(
                Math.Min(PeekBatchSize, MaxCountedMessages - count),
                cancellationToken: cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            count += batch.Count;
        }

        return count;
    }
}
