namespace Payments.Application.Queues;

public interface IQueueStatsReader
{
    Task<QueueStats> ReadAsync(CancellationToken cancellationToken);
}
