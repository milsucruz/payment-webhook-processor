using Azure.Messaging.ServiceBus;
using Payments.Application.DeadLetters;
using Payments.Application.Messaging;

namespace Payments.Infrastructure.Messaging;

// Details: docs/code/ServiceBusDeadLetterQueue.md
public sealed class ServiceBusDeadLetterQueue : IDeadLetterQueue
{
    private const int ReceiveBatchSize = 50;
    private static readonly TimeSpan receiveWaitTime = TimeSpan.FromSeconds(2);

    private readonly ServiceBusClient client;
    private readonly string queueName;
    private readonly IEventPublisher eventPublisher;

    public ServiceBusDeadLetterQueue(ServiceBusClient client, string queueName, IEventPublisher eventPublisher)
    {
        this.client = client;
        this.queueName = queueName;
        this.eventPublisher = eventPublisher;
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

                await this.eventPublisher.PublishAsync(match.MessageId, match.Body.ToString(), cancellationToken);
                await receiver.CompleteMessageAsync(match, cancellationToken);
                return match.MessageId;
            }
        }
        finally
        {
            await Task.WhenAll(skipped.Select(message => AbandonAsync(receiver, message)));
        }
    }

    private static async Task AbandonAsync(ServiceBusReceiver receiver, ServiceBusReceivedMessage message)
    {
        try
        {
            await receiver.AbandonMessageAsync(message, cancellationToken: CancellationToken.None);
        }
        catch (ServiceBusException)
        {
        }
    }

    private ServiceBusReceiver CreateDeadLetterReceiver() =>
        this.client.CreateReceiver(this.queueName, new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.DeadLetter,
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
        });
}
