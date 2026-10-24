using System.Net.Mime;
using Azure.Messaging.ServiceBus;
using Payments.Application.Notifications;

namespace Payments.Infrastructure.Messaging;

public sealed class ServiceBusPaymentNotificationPublisher : IPaymentNotificationPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender sender;

    public ServiceBusPaymentNotificationPublisher(ServiceBusClient client, string queueName)
    {
        this.sender = client.CreateSender(queueName);
    }

    public async Task PublishAsync(PaymentNotification notification, CancellationToken cancellationToken)
    {
        ServiceBusMessage message = new(PaymentNotificationJson.Serialize(notification))
        {
            MessageId = notification.EventId,
            ContentType = MediaTypeNames.Application.Json,
        };

        await this.sender.SendMessageAsync(message, cancellationToken);
    }

    public ValueTask DisposeAsync() => this.sender.DisposeAsync();
}
