using Azure.Identity;
using Azure.Messaging.ServiceBus;

namespace Payments.Infrastructure.Messaging;

public static class ServiceBusClientFactory
{
    public static ServiceBusClient Create(ServiceBusOptions options, ServiceBusClientOptions? clientOptions = null)
    {
        clientOptions ??= new ServiceBusClientOptions();

        if (!string.IsNullOrWhiteSpace(options.FullyQualifiedNamespace))
        {
            return new ServiceBusClient(options.FullyQualifiedNamespace, new DefaultAzureCredential(), clientOptions);
        }

        return new ServiceBusClient(options.ConnectionString, clientOptions);
    }
}
