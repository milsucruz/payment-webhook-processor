using Microsoft.Extensions.Logging;
using Payments.Application.DeadLetters;
using Payments.Application.Tests.Webhooks;

namespace Payments.Application.Tests.DeadLetters;

public class ReprocessDeadLetterTests
{
    [Fact]
    public async Task Known_sequence_number_is_reprocessed_and_logged_as_information()
    {
        InMemoryDeadLetterQueue queue = new();
        queue.DeadLetters.Add(new DeadLetter(7, "evt_1", "InvalidEvent", null, 1, DateTimeOffset.UnixEpoch));
        CollectingLogger<ReprocessDeadLetter> logger = new();
        ReprocessDeadLetter reprocessDeadLetter = new(queue, logger);

        ReprocessDeadLetterResult result = await reprocessDeadLetter.ExecuteAsync(7, CancellationToken.None);

        Assert.Equal(ReprocessDeadLetterResult.Reprocessed, result);
        Assert.Equal(["evt_1"], queue.Reprocessed);
        LogEntry entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("evt_1", entry.Message);
    }

    [Fact]
    public async Task Unknown_sequence_number_is_not_found_and_logged_as_a_warning()
    {
        InMemoryDeadLetterQueue queue = new();
        queue.DeadLetters.Add(new DeadLetter(7, "evt_1", "InvalidEvent", null, 1, DateTimeOffset.UnixEpoch));
        CollectingLogger<ReprocessDeadLetter> logger = new();
        ReprocessDeadLetter reprocessDeadLetter = new(queue, logger);

        ReprocessDeadLetterResult result = await reprocessDeadLetter.ExecuteAsync(8, CancellationToken.None);

        Assert.Equal(ReprocessDeadLetterResult.NotFound, result);
        Assert.Empty(queue.Reprocessed);
        Assert.Equal([LogLevel.Warning], logger.Levels);
    }
}
