namespace Payments.Api.Queues;

public class QueueStatsOptions
{
    public const string SectionName = "QueueStats";

    public TimeSpan Interval { get; set; }
}
