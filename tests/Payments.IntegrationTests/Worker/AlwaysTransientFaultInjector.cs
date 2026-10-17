using Payments.Application.Events;
using Payments.Application.Processing;

namespace Payments.IntegrationTests.Worker;

public sealed class AlwaysTransientFaultInjector : IFaultInjector
{
    public Task InjectAsync(PaymentEvent paymentEvent, int attempt, CancellationToken cancellationToken) =>
        throw new TimeoutException("Simulated database timeout.");
}
