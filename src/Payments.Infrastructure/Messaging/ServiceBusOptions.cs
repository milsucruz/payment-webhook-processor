namespace Payments.Infrastructure.Messaging;

public class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    public string ConnectionString { get; set; } = string.Empty;

    public string EventsQueueName { get; set; } = string.Empty;
}
