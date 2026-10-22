using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Payments.Infrastructure.Messaging;

public sealed class ServiceBusQueueHealthCheck : IHealthCheck
{
    private readonly ServiceBusClient client;
    private readonly string queueName;

    public ServiceBusQueueHealthCheck(ServiceBusClient client, string queueName)
    {
        this.client = client;
        this.queueName = queueName;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        await using ServiceBusReceiver receiver = this.client.CreateReceiver(this.queueName);
        await receiver.PeekMessageAsync(cancellationToken: cancellationToken);

        return HealthCheckResult.Healthy();
    }
}
