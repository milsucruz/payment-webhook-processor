using Azure.Messaging.ServiceBus;
using Payments.Infrastructure.Messaging;

namespace Payments.Infrastructure.Tests.Messaging;

public class ServiceBusClientFactoryTests
{
    private const string ConnectionString = "Endpoint=sb://localhost;SharedAccessKeyName=test;SharedAccessKey=test";

    [Theory]
    [InlineData("", "", false)]
    [InlineData(ConnectionString, "", true)]
    [InlineData("", "sb-test.servicebus.windows.net", true)]
    [InlineData(ConnectionString, "sb-test.servicebus.windows.net", false)]
    public void Options_are_valid_only_with_exactly_one_endpoint(string connectionString, string fullyQualifiedNamespace, bool expected)
    {
        ServiceBusOptions options = new() { ConnectionString = connectionString, FullyQualifiedNamespace = fullyQualifiedNamespace };

        Assert.Equal(expected, options.HasSingleEndpoint);
    }

    [Fact]
    public async Task Namespace_builds_a_client_for_that_namespace()
    {
        ServiceBusOptions options = new() { FullyQualifiedNamespace = "sb-test.servicebus.windows.net" };

        await using ServiceBusClient client = ServiceBusClientFactory.Create(options);

        Assert.Equal("sb-test.servicebus.windows.net", client.FullyQualifiedNamespace);
    }

    [Fact]
    public async Task Connection_string_builds_a_client_for_the_endpoint_in_it()
    {
        ServiceBusOptions options = new() { ConnectionString = ConnectionString };

        await using ServiceBusClient client = ServiceBusClientFactory.Create(options);

        Assert.Equal("localhost", client.FullyQualifiedNamespace);
    }
}
