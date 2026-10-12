using System.Net.Mime;
using Azure.Messaging.ServiceBus;
using Payments.Application.Messaging;

namespace Payments.Infrastructure.Messaging;

public sealed class ServiceBusEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender sender;

    public ServiceBusEventPublisher(ServiceBusClient client, string queueName)
    {
        this.sender = client.CreateSender(queueName);
    }

    public async Task PublishAsync(string eventId, string json, CancellationToken cancellationToken)
    {
        ServiceBusMessage message = new(json)
        {
            MessageId = eventId,
            ContentType = MediaTypeNames.Application.Json,
        };

        await this.sender.SendMessageAsync(message, cancellationToken);
    }

    public ValueTask DisposeAsync() => this.sender.DisposeAsync();
}
