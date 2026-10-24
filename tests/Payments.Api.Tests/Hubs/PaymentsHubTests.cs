using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Payments.Api.Hubs;
using Payments.Application.Queues;
using Payments.Application.Reading;
using Payments.Application.Tests.Queues;
using Payments.Domain;

namespace Payments.Api.Tests.Hubs;

public class PaymentsHubTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly TimeSpan waitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan statsInterval = TimeSpan.FromMilliseconds(50);

    private readonly WebApplicationFactory<Program> factory;

    public PaymentsHubTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Connected_client_receives_queue_stats()
    {
        FakeQueueStatsReader reader = new() { Stats = new QueueStats(3, 1) };
        WebApplicationFactory<Program> app = this.CreateApp(reader);
        TaskCompletionSource<JsonElement> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using HubConnection connection = CreateConnection(app.Server);
        connection.On<JsonElement>(PaymentsHubMethods.QueueStats, message => received.TrySetResult(message));

        await connection.StartAsync();
        JsonElement stats = await received.Task.WaitAsync(waitTimeout);

        Assert.Equal(3, stats.GetProperty("activeMessages").GetInt32());
        Assert.Equal(1, stats.GetProperty("deadLetteredMessages").GetInt32());
    }

    [Fact]
    public async Task Queue_stats_are_not_read_while_no_client_is_connected()
    {
        FakeQueueStatsReader reader = new();
        WebApplicationFactory<Program> app = this.CreateApp(reader);
        _ = app.Server;

        await Task.Delay(statsInterval * 10);

        Assert.Equal(0, reader.ReadCount);
    }

    [Fact]
    public async Task Unreadable_queue_sends_no_stats_and_keeps_the_api_up()
    {
        FakeQueueStatsReader reader = new() { Failure = new InvalidOperationException("Service Bus is down") };
        WebApplicationFactory<Program> app = this.CreateApp(reader);
        int received = 0;
        await using HubConnection connection = CreateConnection(app.Server);
        connection.On<JsonElement>(PaymentsHubMethods.QueueStats, _ => Interlocked.Increment(ref received));

        await connection.StartAsync();
        using CancellationTokenSource timeout = new(waitTimeout);
        while (reader.ReadCount < 3)
        {
            await Task.Delay(statsInterval, timeout.Token);
        }

        Assert.Equal(0, Volatile.Read(ref received));
        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task Payment_status_is_sent_as_a_string()
    {
        WebApplicationFactory<Program> app = this.CreateApp(new FakeQueueStatsReader());
        TaskCompletionSource<JsonElement> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using HubConnection connection = CreateConnection(app.Server);
        connection.On<JsonElement>(PaymentsHubMethods.PaymentUpdated, message => received.TrySetResult(message));
        await connection.StartAsync();
        IHubContext<PaymentsHub> hubContext = app.Services.GetRequiredService<IHubContext<PaymentsHub>>();
        PaymentSummary payment = new("pi_1", PaymentStatus.PartiallyRefunded, 2500, 500, "brl", DateTimeOffset.UnixEpoch);

        await hubContext.Clients.All.SendAsync(PaymentsHubMethods.PaymentUpdated, payment);
        JsonElement message = await received.Task.WaitAsync(waitTimeout);

        Assert.Equal("pi_1", message.GetProperty("paymentIntentId").GetString());
        Assert.Equal("PartiallyRefunded", message.GetProperty("status").GetString());
        Assert.Equal(2500, message.GetProperty("amount").GetInt64());
    }

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

    private WebApplicationFactory<Program> CreateApp(IQueueStatsReader reader) =>
        this.factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Stripe:WebhookSecret", "whsec_test_secret");
            builder.UseSetting("ServiceBus:ConnectionString", "Endpoint=sb://localhost;SharedAccessKeyName=test;SharedAccessKey=test");
            builder.UseSetting("ServiceBus:EventsQueueName", "payment-events");
            builder.UseSetting("ServiceBus:NotificationsQueueName", "payment-notifications");
            builder.UseSetting("ConnectionStrings:Payments", "Server=localhost;Database=Payments");
            builder.UseSetting("Admin:ApiKey", "test-admin-key");
            builder.UseSetting("QueueStats:Interval", statsInterval.ToString());
            builder.ConfigureTestServices(services => services.AddSingleton(reader));
        });
}
