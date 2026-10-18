namespace Payments.Application.DeadLetters;

public interface IDeadLetterQueue
{
    Task<IReadOnlyList<DeadLetter>> PeekAsync(int maxMessages, CancellationToken cancellationToken);

    Task<string?> ReprocessAsync(long sequenceNumber, CancellationToken cancellationToken);
}
