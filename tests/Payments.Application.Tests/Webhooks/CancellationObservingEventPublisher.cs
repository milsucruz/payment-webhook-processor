using Payments.Application.Messaging;

namespace Payments.Application.Tests.Webhooks;

public sealed class CancellationObservingEventPublisher : IEventPublisher
{
    public Task PublishAsync(string eventId, string json, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
