namespace Payments.Api.Hubs;

public sealed class HubConnectionCounter
{
    private int count;

    public int Count => Volatile.Read(ref this.count);

    public void Increment() => Interlocked.Increment(ref this.count);

    public void Decrement() => Interlocked.Decrement(ref this.count);
}
