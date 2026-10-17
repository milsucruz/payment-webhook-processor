using Payments.Application.Events;
using Payments.Application.Processing;

namespace Payments.Application.Tests.Processing;

public sealed class TestFaultInjector : IFaultInjector
{
    public Queue<Exception> Exceptions { get; } = new();

    public List<(string EventId, int Attempt)> Calls { get; } = [];

    public Action? OnInject { get; set; }

    public Task InjectAsync(PaymentEvent paymentEvent, int attempt, CancellationToken cancellationToken)
    {
        this.Calls.Add((paymentEvent.EventId, attempt));
        this.OnInject?.Invoke();
        if (this.Exceptions.TryDequeue(out Exception? exception))
        {
            throw exception;
        }

        return Task.CompletedTask;
    }
}
