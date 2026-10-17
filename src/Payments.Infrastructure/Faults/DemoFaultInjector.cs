using Microsoft.Extensions.Logging;
using Payments.Application.Events;
using Payments.Application.Processing;

namespace Payments.Infrastructure.Faults;

// Details: docs/code/DemoFaultInjector.md
public sealed class DemoFaultInjector : IFaultInjector
{
    private const int TransientFailedAttempts = 2;

    private static readonly TimeSpan slowDelay = TimeSpan.FromSeconds(10);

    private readonly TimeProvider timeProvider;
    private readonly ILogger<DemoFaultInjector> logger;

    public DemoFaultInjector(TimeProvider timeProvider, ILogger<DemoFaultInjector> logger)
    {
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public async Task InjectAsync(PaymentEvent paymentEvent, int attempt, CancellationToken cancellationToken)
    {
        switch (paymentEvent.DemoFault)
        {
            case null:
                return;
            case "transient" when attempt <= TransientFailedAttempts:
                this.LogInjected(paymentEvent, attempt);
                throw new TimeoutException($"Demo fault 'transient' on attempt {attempt} of event {paymentEvent.EventId}.");
            case "transient":
                return;
            case "poison":
                this.LogInjected(paymentEvent, attempt);
                throw new InvalidOperationException($"Demo fault 'poison' on attempt {attempt} of event {paymentEvent.EventId}.");
            case "slow" when attempt == 1:
                this.LogInjected(paymentEvent, attempt);
                await Task.Delay(slowDelay, this.timeProvider, cancellationToken);
                return;
            case "slow":
                return;
            default:
                if (attempt == 1)
                {
                    this.logger.LogWarning(
                        "Ignored unknown demo fault '{DemoFault}' on event {EventId}",
                        paymentEvent.DemoFault,
                        paymentEvent.EventId);
                }

                return;
        }
    }

    private void LogInjected(PaymentEvent paymentEvent, int attempt)
    {
        this.logger.LogWarning(
            "Injecting demo fault '{DemoFault}' on attempt {Attempt} of event {EventId} for payment {PaymentIntentId}",
            paymentEvent.DemoFault,
            attempt,
            paymentEvent.EventId,
            paymentEvent.PaymentIntentId);
    }
}
