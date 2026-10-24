using Azure.Messaging.ServiceBus;
using Payments.Application.Notifications;
using Payments.Application.Processing;
using Payments.Domain;

namespace Payments.IntegrationTests.Worker;

[Collection(WorkerCollection.Name)]
public class PaymentNotificationsTests : IAsyncLifetime
{
    private readonly WorkerFixture fixture;

    public PaymentNotificationsTests(WorkerFixture fixture)
    {
        this.fixture = fixture;
    }

    public Task InitializeAsync() => this.fixture.PurgeAsync(CancellationToken.None);

    public Task DisposeAsync() => this.fixture.StopWorkersAsync(CancellationToken.None);

    [Fact]
    public async Task Processed_event_publishes_a_notification_that_expires_in_60_seconds()
    {
        const string EventId = "evt_notification_succeeded";
        const string PaymentIntentId = "pi_notification";
        await this.fixture.StartWorkerAsync(loggerProvider: null, faultInjection: false, overrideServices: null, CancellationToken.None);

        await this.fixture.SendAsync(EventId, Event(EventId, PaymentIntentId), CancellationToken.None);
        ServiceBusReceivedMessage message = await this.fixture.ReceiveNotificationAsync(EventId, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(60), message.ExpiresAt - message.EnqueuedTime);
        PaymentNotification notification = PaymentNotificationJson.Deserialize(message.Body.ToString());
        Assert.Equal(EventId, notification.EventId);
        Assert.Equal("payment_intent.succeeded", notification.EventType);
        Assert.Equal(PaymentIntentId, notification.PaymentIntentId);
        Assert.Equal(ProcessStripeEventOutcome.Applied, notification.Outcome);
        Assert.NotNull(notification.Payment);
        Assert.Equal(PaymentStatus.Paid, notification.Payment.Status);
        Assert.Equal(2000, notification.Payment.Amount);
    }

    private static string Event(string eventId, string paymentIntentId) =>
        $$"""
        {"id":"{{eventId}}","type":"payment_intent.succeeded","created":1792181099,
         "data":{"object":{"id":"{{paymentIntentId}}","object":"payment_intent","amount":2000,"currency":"usd","status":"succeeded","metadata":{} } } }
        """;
}
