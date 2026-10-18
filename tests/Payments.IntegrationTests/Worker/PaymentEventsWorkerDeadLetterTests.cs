using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;

namespace Payments.IntegrationTests.Worker;

[Collection(WorkerCollection.Name)]
public class PaymentEventsWorkerDeadLetterTests : IAsyncLifetime
{
    private readonly WorkerFixture fixture;

    public PaymentEventsWorkerDeadLetterTests(WorkerFixture fixture)
    {
        this.fixture = fixture;
    }

    public Task InitializeAsync() => this.fixture.PurgeAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Unreadable_event_is_dead_lettered_on_the_first_delivery_with_the_reason()
    {
        using IHost worker = await this.fixture.StartWorkerAsync(new CollectingLoggerProvider());

        await this.fixture.SendAsync("evt_unreadable", "not json");
        ServiceBusReceivedMessage deadLettered = await this.fixture.PeekDeadLetterAsync();
        await worker.StopAsync();

        Assert.Equal("evt_unreadable", deadLettered.MessageId);
        Assert.Equal("not json", deadLettered.Body.ToString());
        Assert.Equal("InvalidEvent", deadLettered.DeadLetterReason);
        Assert.Equal("The event body is not valid JSON.", deadLettered.DeadLetterErrorDescription);
        Assert.Equal(0, deadLettered.DeliveryCount);
    }
}
