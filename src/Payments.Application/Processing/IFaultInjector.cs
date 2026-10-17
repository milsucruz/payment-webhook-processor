using Payments.Application.Events;

namespace Payments.Application.Processing;

public interface IFaultInjector
{
    Task InjectAsync(PaymentEvent paymentEvent, int attempt, CancellationToken cancellationToken);
}
