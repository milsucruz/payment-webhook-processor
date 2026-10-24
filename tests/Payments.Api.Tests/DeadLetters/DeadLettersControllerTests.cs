using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Payments.Application.DeadLetters;
using Payments.Application.Tests.DeadLetters;

namespace Payments.Api.Tests.DeadLetters;

public class DeadLettersControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminKey = "test-admin-key";

    private readonly WebApplicationFactory<Program> factory;

    public DeadLettersControllerTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task List_returns_the_dead_letters_without_the_admin_key()
    {
        InMemoryDeadLetterQueue queue = new();
        DeadLetter deadLetter = new(7, "evt_1", "MaxDeliveryCountExceeded", "Message could not be consumed after 5 delivery attempts.", "Event evt_1 failed with a transient error on all 4 attempts: timeout", 5, DateTimeOffset.UnixEpoch);
        queue.DeadLetters.Add(deadLetter);
        HttpClient client = this.CreateClient(queue);

        HttpResponseMessage response = await client.GetAsync("/api/dead-letters");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<DeadLetter>? body = await response.Content.ReadFromJsonAsync<List<DeadLetter>>();
        Assert.Equal([deadLetter], body);
        Assert.Single(queue.DeadLetters);
    }

    [Fact]
    public async Task Reprocess_with_the_admin_key_returns_202_and_reprocesses_the_message()
    {
        InMemoryDeadLetterQueue queue = new();
        queue.DeadLetters.Add(new DeadLetter(7, "evt_1", "InvalidEvent", null, null, 1, DateTimeOffset.UnixEpoch));
        HttpClient client = this.CreateClient(queue);

        HttpResponseMessage response = await client.SendAsync(ReprocessRequest(7, AdminKey));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(["evt_1"], queue.Reprocessed);
    }

    [Fact]
    public async Task Reprocess_of_an_unknown_sequence_number_returns_404()
    {
        HttpClient client = this.CreateClient(new InMemoryDeadLetterQueue());

        HttpResponseMessage response = await client.SendAsync(ReprocessRequest(7, AdminKey));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key")]
    [InlineData("test-admin-key ")]
    public async Task Reprocess_without_the_right_admin_key_returns_401_and_reprocesses_nothing(string? key)
    {
        InMemoryDeadLetterQueue queue = new();
        queue.DeadLetters.Add(new DeadLetter(7, "evt_1", "InvalidEvent", null, null, 1, DateTimeOffset.UnixEpoch));
        HttpClient client = this.CreateClient(queue);

        HttpResponseMessage response = await client.SendAsync(ReprocessRequest(7, key));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(queue.Reprocessed);
        Assert.Single(queue.DeadLetters);
    }

    private HttpClient CreateClient(IDeadLetterQueue queue) =>
        this.factory
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Stripe:WebhookSecret", "whsec_test_secret");
                builder.UseSetting("ServiceBus:ConnectionString", "Endpoint=sb://localhost;SharedAccessKeyName=test;SharedAccessKey=test");
                builder.UseSetting("ServiceBus:EventsQueueName", "payment-events");
                builder.UseSetting("ServiceBus:NotificationsQueueName", "payment-notifications");
                builder.UseSetting("ConnectionStrings:Payments", "Server=localhost;Database=Payments");
                builder.UseSetting("Admin:ApiKey", AdminKey);
                builder.ConfigureTestServices(services => services.AddSingleton(queue));
            })
            .CreateClient();

    private static HttpRequestMessage ReprocessRequest(long sequenceNumber, string? key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/api/dead-letters/{sequenceNumber}/reprocess");
        if (key is not null)
        {
            request.Headers.Add("X-Admin-Key", key);
        }

        return request;
    }
}
