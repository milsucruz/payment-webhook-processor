using Azure.Messaging.ServiceBus;
using Payments.Application.DeadLetters;

namespace Payments.Infrastructure.Messaging;

// Details: docs/code/ServiceBusDeadLetterQueue.md
public sealed class ServiceBusDeadLetterQueue : IDeadLetterQueue, IAsyncDisposable
{
    private const int ReceiveBatchSize = 50;
    private static readonly TimeSpan receiveWaitTime = TimeSpan.FromSeconds(2);

    private readonly ServiceBusClient client;
    private readonly string queueName;
    private readonly ServiceBusSender sender;

    public ServiceBusDeadLetterQueue(ServiceBusClient client, string queueName)
    {
        this.client = client;
        this.queueName = queueName;
        this.sender = client.CreateSender(queueName);
    }

    public async Task<IReadOnlyList<DeadLetter>> PeekAsync(int maxMessages, CancellationToken cancellationToken)
    {
        await using ServiceBusReceiver receiver = this.CreateDeadLetterReceiver();
        List<DeadLetter> deadLetters = [];

        while (deadLetters.Count < maxMessages)
        {
            IReadOnlyList<ServiceBusReceivedMessage> batch = await receiver.PeekMessagesAsync(
                maxMessages - deadLetters.Count,
                cancellationToken: cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            deadLetters.AddRange(batch.Select(message => new DeadLetter(
                message.SequenceNumber,
                message.MessageId,
                message.DeadLetterReason,
                message.DeadLetterErrorDescription,
                message.DeliveryCount,
                message.EnqueuedTime)));
        }

        return deadLetters;
    }

    public async Task<string?> ReprocessAsync(long sequenceNumber, CancellationToken cancellationToken)
    {
        await using ServiceBusReceiver receiver = this.CreateDeadLetterReceiver();
        List<ServiceBusReceivedMessage> skipped = [];

        try
        {
            while (true)
            {
                IReadOnlyList<ServiceBusReceivedMessage> batch = await receiver.ReceiveMessagesAsync(
                    ReceiveBatchSize,
                    receiveWaitTime,
                    cancellationToken);
                if (batch.Count == 0)
                {
                    return null;
                }

                ServiceBusReceivedMessage? match = batch.FirstOrDefault(message => message.SequenceNumber == sequenceNumber);
                skipped.AddRange(batch.Where(message => message != match));
                if (match is null)
                {
                    continue;
                }

                ServiceBusMessage copy = new(match.Body)
                {
                    MessageId = match.MessageId,
                    ContentType = match.ContentType,
                };
                await this.sender.SendMessageAsync(copy, cancellationToken);
                await receiver.CompleteMessageAsync(match, cancellationToken);
                return match.MessageId;
            }
        }
        finally
        {
            foreach (ServiceBusReceivedMessage message in skipped)
            {
                await receiver.AbandonMessageAsync(message, cancellationToken: CancellationToken.None);
            }
        }
    }

    public ValueTask DisposeAsync() => this.sender.DisposeAsync();

    private ServiceBusReceiver CreateDeadLetterReceiver() =>
        this.client.CreateReceiver(this.queueName, new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.DeadLetter,
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
        });
}
