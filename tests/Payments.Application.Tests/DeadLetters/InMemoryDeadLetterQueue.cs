using Payments.Application.DeadLetters;

namespace Payments.Application.Tests.DeadLetters;

public sealed class InMemoryDeadLetterQueue : IDeadLetterQueue
{
    public List<DeadLetter> DeadLetters { get; } = [];

    public List<string> Reprocessed { get; } = [];

    public int? RequestedMaxMessages { get; private set; }

    public Task<IReadOnlyList<DeadLetter>> PeekAsync(int maxMessages, CancellationToken cancellationToken)
    {
        this.RequestedMaxMessages = maxMessages;
        return Task.FromResult<IReadOnlyList<DeadLetter>>(this.DeadLetters.Take(maxMessages).ToList());
    }

    public Task<string?> ReprocessAsync(long sequenceNumber, CancellationToken cancellationToken)
    {
        DeadLetter? deadLetter = this.DeadLetters.SingleOrDefault(candidate => candidate.SequenceNumber == sequenceNumber);
        if (deadLetter is null)
        {
            return Task.FromResult<string?>(null);
        }

        this.DeadLetters.Remove(deadLetter);
        this.Reprocessed.Add(deadLetter.EventId);
        return Task.FromResult<string?>(deadLetter.EventId);
    }
}
