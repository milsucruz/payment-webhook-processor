using Payments.Application.DeadLetters;

namespace Payments.Application.Tests.DeadLetters;

public class ListDeadLettersTests
{
    [Fact]
    public async Task Returns_the_peeked_messages_and_asks_for_at_most_100()
    {
        InMemoryDeadLetterQueue queue = new();
        DeadLetter deadLetter = new(7, "evt_1", "InvalidEvent", "The event body is not valid JSON.", 1, DateTimeOffset.UnixEpoch);
        queue.DeadLetters.Add(deadLetter);
        ListDeadLetters listDeadLetters = new(queue);

        IReadOnlyList<DeadLetter> result = await listDeadLetters.ExecuteAsync(CancellationToken.None);

        Assert.Equal([deadLetter], result);
        Assert.Equal(100, queue.RequestedMaxMessages);
    }
}
