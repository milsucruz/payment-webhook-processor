using Payments.Application.Events;
using Payments.Application.Processing;

namespace Payments.Infrastructure.Faults;

// Details: docs/code/DemoFaultInjector.md
public sealed class NoFaultInjector : IFaultInjector
{
    public Task InjectAsync(PaymentEvent paymentEvent, int attempt, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
