using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Payments.Api.Hubs;
using Payments.Application.Queues;

namespace Payments.Api.Queues;

// Details: docs/code/QueueStatsBroadcaster.md
public sealed class QueueStatsBroadcaster : BackgroundService
{
    private readonly GetQueueStats getQueueStats;
    private readonly IHubContext<PaymentsHub> hubContext;
    private readonly HubConnectionCounter connectionCounter;
    private readonly TimeSpan interval;
    private readonly TimeProvider timeProvider;

    public QueueStatsBroadcaster(
        GetQueueStats getQueueStats,
        IHubContext<PaymentsHub> hubContext,
        HubConnectionCounter connectionCounter,
        IOptions<QueueStatsOptions> options,
        TimeProvider timeProvider)
    {
        this.getQueueStats = getQueueStats;
        this.hubContext = hubContext;
        this.connectionCounter = connectionCounter;
        this.interval = options.Value.Interval;
        this.timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(this.interval, this.timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (this.connectionCounter.Count == 0)
            {
                continue;
            }

            GetQueueStatsResult result = await this.getQueueStats.ExecuteAsync(stoppingToken);
            if (result.Outcome == GetQueueStatsOutcome.Read)
            {
                await this.hubContext.Clients.All.SendAsync(PaymentsHubMethods.QueueStats, result.Stats, stoppingToken);
            }
        }
    }
}
