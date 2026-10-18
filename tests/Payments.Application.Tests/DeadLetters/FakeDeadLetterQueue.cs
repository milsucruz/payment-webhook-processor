using Payments.Application.DeadLetters;

namespace Payments.Application.Tests.DeadLetters;

public sealed class FakeDeadLetterQueue : IDeadLetterQueue
{
    public List<DeadLetterMessage> Messages { get; } = [];

    public List<int> PeekRequests { get; } = [];

    public List<long> Reprocessed { get; } = [];

    public HashSet<long> Locked { get; } = [];

    public Task<IReadOnlyList<DeadLetterMessage>> PeekAsync(int maxMessages, CancellationToken cancellationToken)
    {
        this.PeekRequests.Add(maxMessages);
        return Task.FromResult<IReadOnlyList<DeadLetterMessage>>(this.Messages.Take(maxMessages).ToList());
    }

    public Task<string?> ReprocessAsync(long sequenceNumber, CancellationToken cancellationToken)
    {
        DeadLetterMessage? message = this.Messages.SingleOrDefault(candidate => candidate.SequenceNumber == sequenceNumber);
        if (message is null || this.Locked.Contains(sequenceNumber))
        {
            return Task.FromResult<string?>(null);
        }

        this.Messages.Remove(message);
        this.Reprocessed.Add(sequenceNumber);
        return Task.FromResult<string?>(message.MessageId);
    }

    public Task<bool> ContainsAsync(long sequenceNumber, CancellationToken cancellationToken) =>
        Task.FromResult(this.Messages.Any(message => message.SequenceNumber == sequenceNumber));

    public static DeadLetterMessage Message(long sequenceNumber, string messageId) =>
        new(sequenceNumber, messageId, "InvalidEvent", "The event body is not valid JSON.", null, 1, DateTimeOffset.UnixEpoch);
}
