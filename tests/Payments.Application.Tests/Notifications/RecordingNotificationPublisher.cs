using Payments.Application.Notifications;

namespace Payments.Application.Tests.Notifications;

public sealed class RecordingNotificationPublisher : IPaymentNotificationPublisher
{
    public List<PaymentNotification> Published { get; } = [];

    public Exception? FailWith { get; set; }

    public Action? OnPublish { get; set; }

    public Task PublishAsync(PaymentNotification notification, CancellationToken cancellationToken)
    {
        this.OnPublish?.Invoke();
        if (this.FailWith is not null)
        {
            throw this.FailWith;
        }

        this.Published.Add(notification);
        return Task.CompletedTask;
    }
}
