using System.Diagnostics.Metrics;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Payments.Application.Telemetry;
using Payments.Application.Tests.Telemetry;
using Payments.Infrastructure.Persistence;

namespace Payments.IntegrationTests.Worker;

[Collection(WorkerCollection.Name)]
public class PaymentEventsWorkerDeadLetterTests : IAsyncLifetime
{
    private readonly WorkerFixture fixture;

    public PaymentEventsWorkerDeadLetterTests(WorkerFixture fixture)
    {
        this.fixture = fixture;
    }

    public Task InitializeAsync() => this.fixture.PurgeAsync(CancellationToken.None);

    public Task DisposeAsync() => this.fixture.StopWorkersAsync(CancellationToken.None);

    [Fact]
    public async Task Unreadable_event_is_dead_lettered_on_the_first_delivery_with_the_reason()
    {
        await this.fixture.StartWorkerAsync(loggerProvider: null, faultInjection: false, overrideServices: null, CancellationToken.None);

        await this.fixture.SendAsync("evt_unreadable", "not json", CancellationToken.None);
        ServiceBusReceivedMessage deadLettered = await this.fixture.PeekDeadLetterAsync(CancellationToken.None);
        await this.fixture.StopWorkersAsync(CancellationToken.None);

        Assert.Equal("evt_unreadable", deadLettered.MessageId);
        Assert.Equal("not json", deadLettered.Body.ToString());
        Assert.Equal("InvalidEvent", deadLettered.DeadLetterReason);
        Assert.Equal("The event body is not valid JSON.", deadLettered.DeadLetterErrorDescription);
        Assert.Equal(0, deadLettered.DeliveryCount);
        await using PaymentsDbContext dbContext = this.fixture.CreateDbContext();
        Assert.False(await dbContext.InboxEvents.AnyAsync(inbox => inbox.EventId == "evt_unreadable"));
    }

    [Fact]
    public async Task Dead_lettered_event_is_counted_with_its_reason()
    {
        using TestMeterFactory meterFactory = new();
        using MetricCollector<long> deadLettered = new(meterFactory, PaymentsMetrics.MeterName, "events.dead_lettered");
        await this.fixture.StartWorkerAsync(
            loggerProvider: null,
            faultInjection: false,
            overrideServices: services => services.AddSingleton<IMeterFactory>(meterFactory),
            CancellationToken.None);

        await this.fixture.SendAsync("evt_unreadable_counted", "not json", CancellationToken.None);
        await this.fixture.PeekDeadLetterAsync(CancellationToken.None);
        await this.fixture.StopWorkersAsync(CancellationToken.None);

        CollectedMeasurement<long> measurement = Assert.Single(deadLettered.GetMeasurementSnapshot());
        Assert.Equal(1, measurement.Value);
        Assert.Equal("InvalidEvent", measurement.Tags["payments.dead_letter_reason"]);
    }
}
