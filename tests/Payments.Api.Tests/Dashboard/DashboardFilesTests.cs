using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Payments.Api.Tests.Dashboard;

public class DashboardFilesTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public DashboardFilesTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Stripe:WebhookSecret", "whsec_test_secret");
            builder.UseSetting("ServiceBus:ConnectionString", "Endpoint=sb://localhost;SharedAccessKeyName=test;SharedAccessKey=test");
            builder.UseSetting("ServiceBus:EventsQueueName", "payment-events");
            builder.UseSetting("ServiceBus:NotificationsQueueName", "payment-notifications");
            builder.UseSetting("ConnectionStrings:Payments", "Server=localhost;Database=Payments");
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
    public async Task Root_serves_the_dashboard_page()
    {
        HttpResponseMessage response = await this.client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<script type=\"module\" src=\"app.js\"></script>", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/app.js", "text/javascript")]
    [InlineData("/api.js", "text/javascript")]
    [InlineData("/dom.js", "text/javascript")]
    [InlineData("/format.js", "text/javascript")]
    [InlineData("/pager.js", "text/javascript")]
    [InlineData("/payments.js", "text/javascript")]
    [InlineData("/payment-detail.js", "text/javascript")]
    [InlineData("/live.js", "text/javascript")]
    [InlineData("/live-feed.js", "text/javascript")]
    [InlineData("/dead-letters.js", "text/javascript")]
    [InlineData("/lib/signalr.min.js", "text/javascript")]
    [InlineData("/styles.css", "text/css")]
    public async Task Dashboard_files_are_served_and_revalidated_on_every_load(string path, string mediaType)
    {
        HttpResponseMessage response = await this.client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.NotNull(response.Headers.ETag);
    }
}
