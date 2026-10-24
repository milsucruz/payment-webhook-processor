using Azure.Messaging.ServiceBus;
using Payments.Application.Notifications;

namespace Payments.Api.Notifications;

// Details: docs/code/PaymentNotificationsListener.md
public sealed class PaymentNotificationsListener : BackgroundService
{
    private readonly ServiceBusProcessor processor;
    private readonly ILogger<PaymentNotificationsListener> logger;

    public PaymentNotificationsListener(ServiceBusProcessor processor, ILogger<PaymentNotificationsListener> logger)
    {
        this.processor = processor;
        this.logger = logger;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await this.processor.StopProcessingAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.processor.ProcessMessageAsync += this.ProcessMessageAsync;
        this.processor.ProcessErrorAsync += this.ProcessErrorAsync;
        await this.processor.StartProcessingAsync(stoppingToken);
    }

    private Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        PaymentNotification notification = PaymentNotificationJson.Deserialize(args.Message.Body.ToString());
        this.logger.LogInformation(
            "Received notification for event {EventId} on payment {PaymentIntentId}: {Outcome}",
            notification.EventId,
            notification.PaymentIntentId,
            notification.Outcome);
        return Task.CompletedTask;
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        this.logger.LogError(
            args.Exception,
            "Error processing {EntityPath} from {ErrorSource}",
            args.EntityPath,
            args.ErrorSource);
        return Task.CompletedTask;
    }
}
