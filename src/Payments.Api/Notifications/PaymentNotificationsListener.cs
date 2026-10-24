using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.SignalR;
using Payments.Api.Hubs;
using Payments.Application.Notifications;

namespace Payments.Api.Notifications;

// Details: docs/code/PaymentNotificationsListener.md
public sealed class PaymentNotificationsListener : BackgroundService
{
    public const string ServiceKey = "payment-notifications";

    private readonly ServiceBusProcessor processor;
    private readonly IHubContext<PaymentsHub> hubContext;
    private readonly ILogger<PaymentNotificationsListener> logger;

    public PaymentNotificationsListener(
        [FromKeyedServices(ServiceKey)] ServiceBusProcessor processor,
        IHubContext<PaymentsHub> hubContext,
        ILogger<PaymentNotificationsListener> logger)
    {
        this.processor = processor;
        this.hubContext = hubContext;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.processor.ProcessMessageAsync += this.ProcessMessageAsync;
        this.processor.ProcessErrorAsync += this.ProcessErrorAsync;
        await this.processor.StartProcessingAsync(stoppingToken);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        PaymentNotification notification = PaymentNotificationJson.Deserialize(args.Message.Body.ToString());
        if (notification.Payment is not null)
        {
            await this.hubContext.Clients.All.SendAsync(PaymentsHubMethods.PaymentUpdated, notification.Payment, args.CancellationToken);
        }

        EventProcessedMessage eventProcessed = new(
            notification.EventId,
            notification.EventType,
            notification.PaymentIntentId,
            notification.Outcome,
            notification.TraceId);
        await this.hubContext.Clients.All.SendAsync(PaymentsHubMethods.EventProcessed, eventProcessed, args.CancellationToken);

        this.logger.LogInformation(
            "Received notification for event {EventId} on payment {PaymentIntentId}: {Outcome}",
            notification.EventId,
            notification.PaymentIntentId,
            notification.Outcome);
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
