using Azure.Messaging.ServiceBus;
using Payments.Application.DeadLetters;

namespace Payments.Infrastructure.Messaging;

public sealed class ServiceBusDeadLetterQueue : IDeadLetterQueue, IAsyncDisposable
{
    private const string AbandonReasonProperty = "AbandonReason";
    private const int MaxScannedMessages = 500;
    private const int ReceiveBatchSize = 50;
    private static readonly TimeSpan receiveWait = TimeSpan.FromSeconds(1);

    private readonly ServiceBusClient client;
    private readonly string queueName;
    private readonly ServiceBusSender sender;

    public ServiceBusDeadLetterQueue(ServiceBusClient client, string queueName)
    {
        this.client = client;
        this.queueName = queueName;
        this.sender = client.CreateSender(queueName);
    }

    public async Task<IReadOnlyList<DeadLetterMessage>> PeekAsync(int maxMessages, CancellationToken cancellationToken)
    {
        await using ServiceBusReceiver receiver = this.CreateDeadLetterReceiver();
        List<DeadLetterMessage> messages = [];
        while (messages.Count < maxMessages)
        {
            IReadOnlyList<ServiceBusReceivedMessage> batch = await receiver.PeekMessagesAsync(
                maxMessages - messages.Count,
                cancellationToken: cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            messages.AddRange(batch.Select(ToDeadLetterMessage));
        }

        return messages;
    }

    public async Task<string?> ReprocessAsync(long sequenceNumber, CancellationToken cancellationToken)
    {
        await using ServiceBusReceiver receiver = this.CreateDeadLetterReceiver();
        List<ServiceBusReceivedMessage> others = [];
        ServiceBusReceivedMessage? match = null;
        bool completed = false;
        try
        {
            while (match is null && others.Count < MaxScannedMessages)
            {
                IReadOnlyList<ServiceBusReceivedMessage> batch = await receiver.ReceiveMessagesAsync(
                    ReceiveBatchSize,
                    receiveWait,
                    cancellationToken);
                if (batch.Count == 0)
                {
                    break;
                }

                foreach (ServiceBusReceivedMessage message in batch)
                {
                    if (message.SequenceNumber == sequenceNumber)
                    {
                        match = message;
                    }
                    else
                    {
                        others.Add(message);
                    }
                }
            }

            if (match is null)
            {
                return null;
            }

            ServiceBusMessage copy = new(match);
            copy.ApplicationProperties.Remove(AbandonReasonProperty);
            await this.sender.SendMessageAsync(copy, cancellationToken);
            await receiver.CompleteMessageAsync(match, CancellationToken.None);
            completed = true;
            return match.MessageId;
        }
        finally
        {
            if (match is not null && !completed)
            {
                others.Add(match);
            }

            await Task.WhenAll(others.Select(message => AbandonQuietlyAsync(receiver, message)));
        }
    }

    public ValueTask DisposeAsync() => this.sender.DisposeAsync();

    private ServiceBusReceiver CreateDeadLetterReceiver() =>
        this.client.CreateReceiver(this.queueName, new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.DeadLetter,
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            PrefetchCount = 0,
        });

    private static async Task AbandonQuietlyAsync(ServiceBusReceiver receiver, ServiceBusReceivedMessage message)
    {
        try
        {
            await receiver.AbandonMessageAsync(message, cancellationToken: CancellationToken.None);
        }
        catch (ServiceBusException)
        {
        }
    }

    private static DeadLetterMessage ToDeadLetterMessage(ServiceBusReceivedMessage message) =>
        new(
            message.SequenceNumber,
            message.MessageId,
            message.DeadLetterReason,
            message.DeadLetterErrorDescription,
            message.ApplicationProperties.TryGetValue(AbandonReasonProperty, out object? abandonReason) ? abandonReason as string : null,
            message.DeliveryCount,
            message.EnqueuedTime);
}
