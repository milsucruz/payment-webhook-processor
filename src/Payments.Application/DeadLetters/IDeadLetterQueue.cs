namespace Payments.Application.DeadLetters;

public interface IDeadLetterQueue
{
    Task<IReadOnlyList<DeadLetterMessage>> PeekAsync(int maxMessages, CancellationToken cancellationToken);

    Task<string?> ReprocessAsync(long sequenceNumber, CancellationToken cancellationToken);
}
