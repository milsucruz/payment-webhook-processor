namespace Payments.Application.Messaging;

public interface IEventPublisher
{
    Task PublishAsync(string eventId, string json, CancellationToken cancellationToken);
}
