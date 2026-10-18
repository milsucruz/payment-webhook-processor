using Microsoft.Extensions.Logging;

namespace Payments.Application.DeadLetters;

// Details: docs/code/ReprocessDeadLetter.md
public sealed class ReprocessDeadLetter
{
    private readonly IDeadLetterQueue deadLetterQueue;
    private readonly ILogger<ReprocessDeadLetter> logger;

    public ReprocessDeadLetter(IDeadLetterQueue deadLetterQueue, ILogger<ReprocessDeadLetter> logger)
    {
        this.deadLetterQueue = deadLetterQueue;
        this.logger = logger;
    }

    public async Task<ReprocessDeadLetterResult> ExecuteAsync(long sequenceNumber, CancellationToken cancellationToken)
    {
        string? eventId = await this.deadLetterQueue.ReprocessAsync(sequenceNumber, cancellationToken);
        if (eventId is null)
        {
            this.logger.LogWarning("No dead-lettered message with sequence number {SequenceNumber}", sequenceNumber);
            return ReprocessDeadLetterResult.NotFound;
        }

        this.logger.LogInformation(
            "Event {EventId} (dead-letter sequence number {SequenceNumber}) was sent back to the event queue",
            eventId,
            sequenceNumber);
        return ReprocessDeadLetterResult.Reprocessed;
    }
}
