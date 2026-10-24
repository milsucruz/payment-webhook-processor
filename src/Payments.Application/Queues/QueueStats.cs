namespace Payments.Application.Queues;

public sealed record QueueStats(int ActiveMessages, int DeadLetteredMessages, int CountLimit);
