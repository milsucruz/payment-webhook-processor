namespace Payments.Application.DeadLetters;

public sealed class ListDeadLetters
{
    public const int MaxMessages = 100;

    private readonly IDeadLetterQueue deadLetterQueue;

    public ListDeadLetters(IDeadLetterQueue deadLetterQueue)
    {
        this.deadLetterQueue = deadLetterQueue;
    }

    public Task<IReadOnlyList<DeadLetter>> ExecuteAsync(CancellationToken cancellationToken) =>
        this.deadLetterQueue.PeekAsync(MaxMessages, cancellationToken);
}
