using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Payments.Api.Controllers;
using Payments.Application.Messaging;
using Payments.Infrastructure.Tests.Webhooks;

namespace Payments.Api.Tests.Webhooks;

public class StripeWebhooksControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Secret = "whsec_test_secret";
    private const string EventJson = """{"id":"evt_1PtTest","object":"event","type":"payment_intent.succeeded"}""";

    private readonly WebApplicationFactory<Program> factory;

    public StripeWebhooksControllerTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Valid_signature_returns_200_and_publishes_the_original_body_with_the_event_id()
    {
        RecordingEventPublisher publisher = new();
        HttpClient client = this.CreateClient(publisher);

        HttpResponseMessage response = await client.SendAsync(SignedRequest(EventJson));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PublishedEvent published = Assert.Single(publisher.Published);
        Assert.Equal("evt_1PtTest", published.EventId);
        Assert.Equal(EventJson, published.Json);
    }

    [Fact]
    public async Task Failing_publisher_returns_500()
    {
        HttpClient client = this.CreateClient(new FailingEventPublisher());

        HttpResponseMessage response = await client.SendAsync(SignedRequest(EventJson));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_signature_returns_400_and_publishes_nothing()
    {
        RecordingEventPublisher publisher = new();
        HttpClient client = this.CreateClient(publisher);
        HttpRequestMessage request = SignedRequest(EventJson);
        request.Headers.Remove("Stripe-Signature");
        request.Headers.Add("Stripe-Signature", StripeSignatureHelper.Header(EventJson, "whsec_wrong_secret", Now()));

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(publisher.Published);
    }

    [Theory]
    [InlineData("""{"object":"event"}""")]
    [InlineData("""{"id":""}""")]
    [InlineData("""{"id":"   "}""")]
    [InlineData("""{"id":123}""")]
    [InlineData("""["evt_1PtTest"]""")]
    [InlineData("not json")]
    public async Task Signed_body_without_a_valid_event_id_returns_400_and_publishes_nothing(string body)
    {
        RecordingEventPublisher publisher = new();
        HttpClient client = this.CreateClient(publisher);

        HttpResponseMessage response = await client.SendAsync(SignedRequest(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Event_id_longer_than_128_characters_returns_400()
    {
        RecordingEventPublisher publisher = new();
        HttpClient client = this.CreateClient(publisher);
        string body = $$"""{"id":"evt_{{new string('a', 125)}}"}""";

        HttpResponseMessage response = await client.SendAsync(SignedRequest(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Request_aborted_during_publish_is_logged_as_a_warning_not_as_a_publish_failure()
    {
        BlockingEventPublisher publisher = new();
        CollectingLoggerProvider logs = new(typeof(StripeWebhooksController).FullName!);
        HttpClient client = this.CreateClient(publisher, logs);
        using CancellationTokenSource abort = new();

        Task<HttpResponseMessage> sending = client.SendAsync(SignedRequest(EventJson), abort.Token);
        await publisher.Started.WaitAsync(TimeSpan.FromSeconds(5));
        abort.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        LogLevel level = await logs.FirstEntry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(LogLevel.Warning, level);
    }

    private HttpClient CreateClient(IEventPublisher publisher, ILoggerProvider? loggerProvider = null) =>
        this.factory
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Stripe:WebhookSecret", Secret);
                builder.UseSetting("ServiceBus:ConnectionString", "Endpoint=sb://localhost;SharedAccessKeyName=test;SharedAccessKey=test");
                builder.UseSetting("ServiceBus:EventsQueueName", "payment-events");
                builder.ConfigureTestServices(services => services.AddSingleton(publisher));
                if (loggerProvider is not null)
                {
                    builder.ConfigureLogging(logging => logging.AddProvider(loggerProvider));
                }
            })
            .CreateClient();

    private static HttpRequestMessage SignedRequest(string body)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", StripeSignatureHelper.Header(body, Secret, Now()));
        return request;
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
