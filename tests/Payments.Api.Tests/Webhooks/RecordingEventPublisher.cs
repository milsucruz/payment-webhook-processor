using Payments.Application.Messaging;

namespace Payments.Api.Tests.Webhooks;

public sealed class RecordingEventPublisher : IEventPublisher
{
    public List<PublishedEvent> Published { get; } = [];

    public Task PublishAsync(string eventId, string json, CancellationToken cancellationToken)
    {
        this.Published.Add(new PublishedEvent(eventId, json));
        return Task.CompletedTask;
    }
}

public sealed record PublishedEvent(string EventId, string Json);
