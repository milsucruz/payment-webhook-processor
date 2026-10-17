using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace Payments.Application.Tests.Processing;

public sealed class DelayTrackingTimeProvider : FakeTimeProvider
{
    private readonly Channel<TimeSpan> delays = Channel.CreateUnbounded<TimeSpan>();

    public DelayTrackingTimeProvider(DateTimeOffset startDateTime)
        : base(startDateTime)
    {
    }

    public bool HasUnobservedDelay => this.delays.Reader.TryPeek(out _);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        this.delays.Writer.TryWrite(dueTime);
        return base.CreateTimer(callback, state, dueTime, period);
    }

    public async Task<TimeSpan> WaitForDelay()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        return await this.delays.Reader.ReadAsync(timeout.Token);
    }
}
