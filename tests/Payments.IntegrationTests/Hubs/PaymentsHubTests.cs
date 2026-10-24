extern alias api;

using System.Text.Json;
using api::Payments.Api.Hubs;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Payments.IntegrationTests.Worker;

namespace Payments.IntegrationTests.Hubs;

[Collection(WorkerCollection.Name)]
public class PaymentsHubTests : IAsyncLifetime
{
    private static readonly TimeSpan waitTimeout = TimeSpan.FromSeconds(150);

    private readonly WorkerFixture fixture;

    public PaymentsHubTests(WorkerFixture fixture)
    {
        this.fixture = fixture;
    }

    public Task InitializeAsync() => this.fixture.PurgeAsync(CancellationToken.None);

    public Task DisposeAsync() => this.fixture.StopWorkersAsync(CancellationToken.None);

    [Fact]
    public async Task Hub_client_receives_payment_updated_event_processed_and_queue_stats()
    {
        const string EventId = "evt_hub_succeeded";
        const string PaymentIntentId = "pi_hub";
        await using WebApplicationFactory<api::Program> app = this.CreateApi();
        TaskCompletionSource<JsonElement> paymentUpdated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<JsonElement> eventProcessed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<JsonElement> queueStats = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using HubConnection connection = CreateConnection(app.Server);
        connection.On<JsonElement>(PaymentsHubMethods.PaymentUpdated, message =>
        {
            if (message.GetProperty("paymentIntentId").GetString() == PaymentIntentId)
            {
                paymentUpdated.TrySetResult(message);
            }
        });
        connection.On<JsonElement>(PaymentsHubMethods.EventProcessed, message =>
        {
            if (message.GetProperty("eventId").GetString() == EventId)
            {
                eventProcessed.TrySetResult(message);
            }
        });
        connection.On<JsonElement>(PaymentsHubMethods.QueueStats, message => queueStats.TrySetResult(message));
        await connection.StartAsync();
        await this.fixture.StartWorkerAsync(loggerProvider: null, faultInjection: false, overrideServices: null, CancellationToken.None);

        await this.fixture.SendAsync(EventId, Event(EventId, PaymentIntentId), CancellationToken.None);
        JsonElement payment = await paymentUpdated.Task.WaitAsync(waitTimeout);
        JsonElement processed = await eventProcessed.Task.WaitAsync(waitTimeout);
        JsonElement stats = await queueStats.Task.WaitAsync(waitTimeout);

        Assert.Equal("Paid", payment.GetProperty("status").GetString());
        Assert.Equal(2000, payment.GetProperty("amount").GetInt64());
        Assert.Equal("usd", payment.GetProperty("currency").GetString());
        Assert.Equal("payment_intent.succeeded", processed.GetProperty("eventType").GetString());
        Assert.Equal(PaymentIntentId, processed.GetProperty("paymentIntentId").GetString());
        Assert.Equal("Applied", processed.GetProperty("outcome").GetString());
        Assert.True(stats.GetProperty("activeMessages").GetInt32() >= 0);
        Assert.True(stats.GetProperty("deadLetteredMessages").GetInt32() >= 0);
    }

    private static string Event(string eventId, string paymentIntentId) =>
        $$"""
        {"id":"{{eventId}}","type":"payment_intent.succeeded","created":1792181099,
         "data":{"object":{"id":"{{paymentIntentId}}","object":"payment_intent","amount":2000,"currency":"usd","status":"succeeded","metadata":{} } } }
        """;

    private static HubConnection CreateConnection(TestServer server) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, PaymentsHub.Route), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.WebSocketFactory = (context, cancellationToken) =>
                    new ValueTask<System.Net.WebSockets.WebSocket>(server.CreateWebSocketClient().ConnectAsync(context.Uri, cancellationToken));
            })
            .Build();

    private WebApplicationFactory<api::Program> CreateApi() =>
        new WebApplicationFactory<api::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Stripe:WebhookSecret", "whsec_test_secret");
            builder.UseSetting("ServiceBus:ConnectionString", this.fixture.ServiceBusConnectionString);
            builder.UseSetting("ServiceBus:EventsQueueName", WorkerFixture.QueueName);
            builder.UseSetting("ServiceBus:NotificationsQueueName", WorkerFixture.NotificationsQueueName);
            builder.UseSetting("ConnectionStrings:Payments", this.fixture.PaymentsConnectionString);
            builder.UseSetting("Admin:ApiKey", "test-admin-key");
            builder.UseSetting("QueueStats:Interval", "00:00:00.200");
        });
}
