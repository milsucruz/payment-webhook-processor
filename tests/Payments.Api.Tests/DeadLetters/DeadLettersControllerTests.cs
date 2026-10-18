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
    private readonly FakeDeadLetterQueue queue = new();

    public DeadLettersControllerTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task List_returns_the_dead_lettered_messages_without_the_admin_key()
    {
        this.queue.Messages.Add(FakeDeadLetterQueue.Message(7, "evt_1PtTest"));

        HttpResponseMessage response = await this.CreateClient().GetAsync("/api/dead-letters");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        DeadLetterMessage[]? messages = await response.Content.ReadFromJsonAsync<DeadLetterMessage[]>();
        DeadLetterMessage message = Assert.Single(messages!);
        Assert.Equal(7, message.SequenceNumber);
        Assert.Equal("evt_1PtTest", message.MessageId);
        Assert.Equal("InvalidEvent", message.Reason);
        Assert.Single(this.queue.Messages);
    }

    [Fact]
    public async Task Reprocess_with_the_admin_key_returns_204_and_reprocesses_the_message()
    {
        this.queue.Messages.Add(FakeDeadLetterQueue.Message(7, "evt_1PtTest"));

        HttpResponseMessage response = await this.CreateClient().SendAsync(ReprocessRequest(7, AdminKey));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([7L], this.queue.Reprocessed);
    }

    [Fact]
    public async Task Reprocess_of_a_message_not_in_the_dead_letter_returns_404()
    {
        HttpResponseMessage response = await this.CreateClient().SendAsync(ReprocessRequest(7, AdminKey));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reprocess_of_a_message_the_scan_cannot_receive_returns_409()
    {
        this.queue.Messages.Add(FakeDeadLetterQueue.Message(7, "evt_1PtTest"));
        this.queue.Locked.Add(7);

        HttpResponseMessage response = await this.CreateClient().SendAsync(ReprocessRequest(7, AdminKey));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(this.queue.Messages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key")]
    [InlineData("test-admin-key ")]
    public async Task Reprocess_without_the_right_admin_key_returns_401_and_reprocesses_nothing(string? key)
    {
        this.queue.Messages.Add(FakeDeadLetterQueue.Message(7, "evt_1PtTest"));

        HttpResponseMessage response = await this.CreateClient().SendAsync(ReprocessRequest(7, key));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(this.queue.Reprocessed);
        Assert.Single(this.queue.Messages);
    }

    private HttpClient CreateClient() =>
        this.factory
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Stripe:WebhookSecret", "whsec_test_secret");
                builder.UseSetting("ServiceBus:ConnectionString", "Endpoint=sb://localhost;SharedAccessKeyName=test;SharedAccessKey=test");
                builder.UseSetting("ServiceBus:EventsQueueName", "payment-events");
                builder.UseSetting("Admin:ApiKey", AdminKey);
                builder.ConfigureTestServices(services => services.AddSingleton<IDeadLetterQueue>(this.queue));
            })
            .CreateClient();

    private static HttpRequestMessage ReprocessRequest(long sequenceNumber, string? adminKey)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/api/dead-letters/{sequenceNumber}/reprocess");
        if (adminKey is not null)
        {
            request.Headers.Add("X-Admin-Key", adminKey);
        }

        return request;
    }
}
