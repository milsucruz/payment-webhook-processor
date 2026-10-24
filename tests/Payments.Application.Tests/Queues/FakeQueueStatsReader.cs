using Payments.Application.Queues;

namespace Payments.Application.Tests.Queues;

public sealed class FakeQueueStatsReader : IQueueStatsReader
{
    private int readCount;

    public QueueStats Stats { get; set; } = new(0, 0);

    public Exception? Failure { get; set; }

    public int ReadCount => Volatile.Read(ref this.readCount);

    public Task<QueueStats> ReadAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref this.readCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (this.Failure is not null)
        {
            throw this.Failure;
        }

        return Task.FromResult(this.Stats);
    }
}
