using Payments.Application.DeadLetters;

namespace Payments.Application.Tests.DeadLetters;

public class ListDeadLettersTests
{
    [Fact]
    public async Task Returns_the_peeked_messages_asking_for_at_most_100()
    {
        FakeDeadLetterQueue queue = new();
        queue.Messages.Add(FakeDeadLetterQueue.Message(7, "evt_1"));
        queue.Messages.Add(FakeDeadLetterQueue.Message(9, "evt_2"));

        IReadOnlyList<DeadLetterMessage> messages = await new ListDeadLetters(queue).ExecuteAsync(CancellationToken.None);

        Assert.Equal([7L, 9L], messages.Select(message => message.SequenceNumber));
        Assert.Equal([100], queue.PeekRequests);
    }
}
