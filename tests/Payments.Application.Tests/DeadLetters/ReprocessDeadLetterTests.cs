using Microsoft.Extensions.Logging;
using Payments.Application.DeadLetters;
using Payments.Application.Tests.Webhooks;

namespace Payments.Application.Tests.DeadLetters;

public class ReprocessDeadLetterTests
{
    private readonly FakeDeadLetterQueue queue = new();
    private readonly CollectingLogger<ReprocessDeadLetter> logger = new();

    [Fact]
    public async Task Message_in_the_dead_letter_is_reprocessed_and_logged_with_its_event_id()
    {
        this.queue.Messages.Add(FakeDeadLetterQueue.Message(7, "evt_1PtTest"));

        ReprocessDeadLetterResult result = await new ReprocessDeadLetter(this.queue, this.logger).ExecuteAsync(7, CancellationToken.None);

        Assert.Equal(ReprocessDeadLetterResult.Reprocessed, result);
        Assert.Equal([7L], this.queue.Reprocessed);
        LogEntry entry = Assert.Single(this.logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("evt_1PtTest", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_sequence_number_returns_not_found_and_logs_a_warning()
    {
        this.queue.Messages.Add(FakeDeadLetterQueue.Message(7, "evt_1PtTest"));

        ReprocessDeadLetterResult result = await new ReprocessDeadLetter(this.queue, this.logger).ExecuteAsync(8, CancellationToken.None);

        Assert.Equal(ReprocessDeadLetterResult.NotFound, result);
        Assert.Empty(this.queue.Reprocessed);
        LogEntry entry = Assert.Single(this.logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("8", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Message_in_the_dead_letter_that_the_scan_cannot_receive_returns_not_reached_and_logs_a_warning()
    {
        this.queue.Messages.Add(FakeDeadLetterQueue.Message(7, "evt_1PtTest"));
        this.queue.Locked.Add(7);

        ReprocessDeadLetterResult result = await new ReprocessDeadLetter(this.queue, this.logger).ExecuteAsync(7, CancellationToken.None);

        Assert.Equal(ReprocessDeadLetterResult.NotReached, result);
        Assert.Empty(this.queue.Reprocessed);
        LogEntry entry = Assert.Single(this.logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("could not receive", entry.Message, StringComparison.Ordinal);
    }
}
