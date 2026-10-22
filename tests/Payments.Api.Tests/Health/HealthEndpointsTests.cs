using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Payments.Api.Tests.Health;

public class HealthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public HealthEndpointsTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Stripe:WebhookSecret", "whsec_test_secret");
            builder.UseSetting("ServiceBus:ConnectionString", "Endpoint=sb://localhost;SharedAccessKeyName=test;SharedAccessKey=test");
            builder.UseSetting("ServiceBus:EventsQueueName", "payment-events");
            builder.UseSetting("Admin:ApiKey", "test-admin-key");
        });
        this.client = this.factory.CreateClient();
    }

    public void Dispose()
    {
        this.client.Dispose();
        this.factory.Dispose();
    }

    [Fact]
    public async Task Live_returns_200_while_service_bus_is_unreachable()
    {
        HttpResponseMessage response = await this.client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_returns_503_while_service_bus_is_unreachable()
    {
        HttpResponseMessage response = await this.client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }
}
