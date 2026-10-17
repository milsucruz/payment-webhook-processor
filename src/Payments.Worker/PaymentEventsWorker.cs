using Azure.Messaging.ServiceBus;
using Payments.Application.Processing;

namespace Payments.Worker;

// Details: docs/code/PaymentEventsWorker.md
public sealed class PaymentEventsWorker : BackgroundService
{
    private readonly ServiceBusProcessor processor;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<PaymentEventsWorker> logger;

    public PaymentEventsWorker(ServiceBusProcessor processor, IServiceScopeFactory scopeFactory, ILogger<PaymentEventsWorker> logger)
    {
        this.processor = processor;
        this.scopeFactory = scopeFactory;
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

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        await using AsyncServiceScope scope = this.scopeFactory.CreateAsyncScope();
        ProcessStripeEvent processStripeEvent = scope.ServiceProvider.GetRequiredService<ProcessStripeEvent>();

        ProcessStripeEventResult result = await processStripeEvent.ExecuteAsync(args.Message.Body.ToString(), args.CancellationToken);

        switch (result.Outcome)
        {
            case ProcessStripeEventOutcome.Applied:
            case ProcessStripeEventOutcome.IgnoredOutOfOrder:
            case ProcessStripeEventOutcome.Redundant:
            case ProcessStripeEventOutcome.Duplicate:
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                break;
            case ProcessStripeEventOutcome.InvalidEvent:
            case ProcessStripeEventOutcome.Conflict:
            case ProcessStripeEventOutcome.ConcurrencyConflict:
            case ProcessStripeEventOutcome.TransientFailure:
                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(args), result.Outcome, "Unexpected processing outcome.");
        }
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
