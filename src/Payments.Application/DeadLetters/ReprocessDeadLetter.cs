using Microsoft.Extensions.Logging;

namespace Payments.Application.DeadLetters;

public class ReprocessDeadLetter
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
        string? messageId = await this.deadLetterQueue.ReprocessAsync(sequenceNumber, cancellationToken);
        if (messageId is null)
        {
            this.logger.LogWarning("Dead-lettered message {SequenceNumber} was not found", sequenceNumber);
            return ReprocessDeadLetterResult.NotFound;
        }

        this.logger.LogInformation(
            "Dead-lettered message {SequenceNumber} ({MessageId}) was sent back to the event queue",
            sequenceNumber,
            messageId);
        return ReprocessDeadLetterResult.Reprocessed;
    }
}
