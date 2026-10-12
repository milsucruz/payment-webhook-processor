using Azure.Messaging.ServiceBus;
using Payments.Application.Messaging;

namespace Payments.Api.Tests.Webhooks;

public sealed class FailingEventPublisher : IEventPublisher
{
    public Task PublishAsync(string eventId, string json, CancellationToken cancellationToken) =>
        throw new ServiceBusException("The queue is unavailable.", ServiceBusFailureReason.ServiceTimeout);
}
