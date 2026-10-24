using Microsoft.Extensions.Logging;

namespace Payments.Application.Queues;

// Details: docs/code/GetQueueStats.md
public sealed class GetQueueStats
{
    private readonly IQueueStatsReader queueStatsReader;
    private readonly ILogger<GetQueueStats> logger;

    public GetQueueStats(IQueueStatsReader queueStatsReader, ILogger<GetQueueStats> logger)
    {
        this.queueStatsReader = queueStatsReader;
        this.logger = logger;
    }

    public async Task<GetQueueStatsResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            QueueStats stats = await this.queueStatsReader.ReadAsync(cancellationToken);
            return new GetQueueStatsResult(GetQueueStatsOutcome.Read, stats);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            this.logger.LogWarning("Failed to read the queue stats: {Error}", exception.Message);
            return new GetQueueStatsResult(GetQueueStatsOutcome.Unavailable);
        }
    }
}
