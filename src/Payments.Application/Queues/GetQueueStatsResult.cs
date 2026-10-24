namespace Payments.Application.Queues;

public sealed record GetQueueStatsResult(GetQueueStatsOutcome Outcome, QueueStats? Stats = null);
