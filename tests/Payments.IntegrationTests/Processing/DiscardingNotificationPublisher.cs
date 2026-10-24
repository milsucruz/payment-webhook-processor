using Payments.Application.Notifications;

namespace Payments.IntegrationTests.Processing;

public sealed class DiscardingNotificationPublisher : IPaymentNotificationPublisher
{
    public Task PublishAsync(PaymentNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
}
