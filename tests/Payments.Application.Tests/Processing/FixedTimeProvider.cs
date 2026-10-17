namespace Payments.Application.Tests.Processing;

public sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset now;

    public FixedTimeProvider(DateTimeOffset now)
    {
        this.now = now;
    }

    public List<TimeSpan> Delays { get; } = [];

    public override DateTimeOffset GetUtcNow() => this.now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        this.Delays.Add(dueTime);
        callback(state);
        return new FiredTimer();
    }

    private sealed class FiredTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
