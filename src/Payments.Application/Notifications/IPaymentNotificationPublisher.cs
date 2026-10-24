namespace Payments.Application.Notifications;

public interface IPaymentNotificationPublisher
{
    Task PublishAsync(PaymentNotification notification, CancellationToken cancellationToken);
}
