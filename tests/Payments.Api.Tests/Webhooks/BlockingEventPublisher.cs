using Payments.Application.Messaging;

namespace Payments.Api.Tests.Webhooks;

public sealed class BlockingEventPublisher : IEventPublisher
{
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => this.started.Task;

    public async Task PublishAsync(string eventId, string json, CancellationToken cancellationToken)
    {
        this.started.SetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
