using Microsoft.Extensions.Logging;
using Payments.Application.Queues;
using Payments.Application.Tests.Webhooks;

namespace Payments.Application.Tests.Queues;

public class GetQueueStatsTests
{
    [Fact]
    public async Task Readable_queue_returns_its_stats()
    {
        FakeQueueStatsReader reader = new() { Stats = new QueueStats(3, 1, 1000) };
        CollectingLogger<GetQueueStats> logger = new();
        GetQueueStats getQueueStats = new(reader, logger);

        GetQueueStatsResult result = await getQueueStats.ExecuteAsync(CancellationToken.None);

        Assert.Equal(GetQueueStatsOutcome.Read, result.Outcome);
        Assert.Equal(new QueueStats(3, 1, 1000), result.Stats);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Failed_read_is_unavailable_and_logged_as_warning()
    {
        FakeQueueStatsReader reader = new() { Failure = new InvalidOperationException("Service Bus is down") };
        CollectingLogger<GetQueueStats> logger = new();
        GetQueueStats getQueueStats = new(reader, logger);

        GetQueueStatsResult result = await getQueueStats.ExecuteAsync(CancellationToken.None);

        Assert.Equal(GetQueueStatsOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Stats);
        Assert.Equal([LogLevel.Warning], logger.Levels);
    }

    [Fact]
    public async Task Failure_while_the_caller_is_cancelling_is_still_unavailable()
    {
        FakeQueueStatsReader reader = new() { Failure = new InvalidOperationException("Service Bus is down") };
        GetQueueStats getQueueStats = new(reader, new CollectingLogger<GetQueueStats>());
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        GetQueueStatsResult result = await getQueueStats.ExecuteAsync(cancellation.Token);

        Assert.Equal(GetQueueStatsOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task Cancellation_by_the_caller_propagates()
    {
        GetQueueStats getQueueStats = new(new FakeQueueStatsReader(), new CollectingLogger<GetQueueStats>());
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => getQueueStats.ExecuteAsync(cancellation.Token));
    }
}
