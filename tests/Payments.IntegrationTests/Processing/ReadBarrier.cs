namespace Payments.IntegrationTests.Processing;

public sealed class ReadBarrier
{
    private static readonly TimeSpan timeout = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int remaining;

    public ReadBarrier(int participants)
    {
        this.remaining = participants;
    }

    public async Task SignalAndWaitAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Decrement(ref this.remaining) == 0)
        {
            this.allArrived.SetResult();
        }

        await this.allArrived.Task.WaitAsync(timeout, cancellationToken);
    }
}
