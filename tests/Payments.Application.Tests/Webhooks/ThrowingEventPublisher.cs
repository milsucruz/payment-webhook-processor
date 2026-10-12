using Payments.Application.Messaging;

namespace Payments.Application.Tests.Webhooks;

public sealed class ThrowingEventPublisher : IEventPublisher
{
    private readonly Exception exception;

    public ThrowingEventPublisher(Exception exception)
    {
        this.exception = exception;
    }

    public Task PublishAsync(string eventId, string json, CancellationToken cancellationToken) =>
        throw this.exception;
}
