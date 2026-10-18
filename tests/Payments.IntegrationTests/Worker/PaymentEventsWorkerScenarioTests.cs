using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Payments.Application.Processing;
using Payments.Domain;
using Payments.Infrastructure.Persistence;

namespace Payments.IntegrationTests.Worker;

[Collection(WorkerCollection.Name)]
public class PaymentEventsWorkerScenarioTests : IAsyncLifetime
{
    private readonly WorkerFixture fixture;
    private readonly CollectingLoggerProvider logs = new();

    public PaymentEventsWorkerScenarioTests(WorkerFixture fixture)
    {
        this.fixture = fixture;
    }

    public Task InitializeAsync() => this.fixture.PurgeAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Duplicate_delivery_writes_one_row_and_rolls_back()
    {
        const string PaymentIntentId = "pi_duplicate";
        string succeeded = Event("evt_duplicate_succeeded", "payment_intent.succeeded", "succeeded", PaymentIntentId);
        using IHost worker = await this.fixture.StartWorkerAsync(this.logs);

        await this.fixture.SendAsync("evt_duplicate_succeeded", succeeded);
        await this.fixture.WaitUntilQueueIsEmptyAsync();
        byte[] rowVersionBefore = await this.RowVersionAsync(PaymentIntentId);
        await this.fixture.SendAsync("evt_duplicate_succeeded", succeeded);
        await this.fixture.WaitUntilQueueIsEmptyAsync();
        await worker.StopAsync();

        await using PaymentsDbContext dbContext = this.fixture.CreateDbContext();
        Payment payment = await dbContext.Payments.SingleAsync(candidate => candidate.PaymentIntentId == PaymentIntentId);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(1, await dbContext.InboxEvents.CountAsync(inbox => inbox.PaymentIntentId == PaymentIntentId));
        Assert.Equal(1, await dbContext.PaymentHistory.CountAsync(entry => entry.PaymentIntentId == PaymentIntentId));
        Assert.Equal(rowVersionBefore, await this.RowVersionAsync(PaymentIntentId));
        Assert.Contains(this.logs.Messages, message => message.Contains("Event evt_duplicate_succeeded was already processed", StringComparison.Ordinal));
        Assert.Equal(0, await this.fixture.CountDeadLettersAsync());
    }

    [Fact]
    public async Task Poison_message_reaches_the_dead_letter_after_5_deliveries()
    {
        const string PaymentIntentId = "pi_poison";
        using IHost worker = await this.fixture.StartWorkerAsync(this.logs, faultInjection: true);

        await this.fixture.SendAsync(
            "evt_poison_succeeded",
            Event("evt_poison_succeeded", "payment_intent.succeeded", "succeeded", PaymentIntentId, demoFault: "poison"));
        ServiceBusReceivedMessage deadLettered = await this.fixture.PeekDeadLetterAsync();
        await worker.StopAsync();

        Assert.Equal("evt_poison_succeeded", deadLettered.MessageId);
        Assert.Equal("MaxDeliveryCountExceeded", deadLettered.DeadLetterReason);
        Assert.Equal(5, deadLettered.DeliveryCount);
        await using PaymentsDbContext dbContext = this.fixture.CreateDbContext();
        Assert.False(await dbContext.Payments.AnyAsync(payment => payment.PaymentIntentId == PaymentIntentId));
        Assert.False(await dbContext.InboxEvents.AnyAsync(inbox => inbox.PaymentIntentId == PaymentIntentId));
    }

    [Fact]
    public async Task Impossible_conflict_reaches_the_dead_letter_on_the_first_delivery()
    {
        const string PaymentIntentId = "pi_conflict";
        using IHost worker = await this.fixture.StartWorkerAsync(this.logs);

        await this.fixture.SendAsync(
            "evt_conflict_canceled",
            Event("evt_conflict_canceled", "payment_intent.canceled", "canceled", PaymentIntentId));
        await this.fixture.WaitUntilQueueIsEmptyAsync();
        await this.fixture.SendAsync(
            "evt_conflict_succeeded",
            Event("evt_conflict_succeeded", "payment_intent.succeeded", "succeeded", PaymentIntentId));
        ServiceBusReceivedMessage deadLettered = await this.fixture.PeekDeadLetterAsync();
        await worker.StopAsync();

        Assert.Equal("evt_conflict_succeeded", deadLettered.MessageId);
        Assert.Equal("ImpossibleConflict", deadLettered.DeadLetterReason);
        Assert.Equal(
            "Event evt_conflict_succeeded requests Paid for payment pi_conflict, which is Canceled.",
            deadLettered.DeadLetterErrorDescription);
        Assert.Equal(0, deadLettered.DeliveryCount);
        await using PaymentsDbContext dbContext = this.fixture.CreateDbContext();
        Payment payment = await dbContext.Payments.SingleAsync(candidate => candidate.PaymentIntentId == PaymentIntentId);
        Assert.Equal(PaymentStatus.Canceled, payment.Status);
        Assert.Equal(["evt_conflict_canceled"], await dbContext.InboxEvents
            .Where(inbox => inbox.PaymentIntentId == PaymentIntentId)
            .Select(inbox => inbox.EventId)
            .ToListAsync());
        Assert.Equal(1, await dbContext.PaymentHistory.CountAsync(entry => entry.PaymentIntentId == PaymentIntentId));
    }

    [Fact]
    public async Task Crash_after_commit_does_not_duplicate_on_redelivery()
    {
        const string PaymentIntentId = "pi_crash";
        CrashSwitch crashSwitch = new();
        using IHost worker = await this.fixture.StartWorkerAsync(this.logs, overrideServices: services =>
        {
            services.AddScoped<PaymentStore>();
            services.AddScoped<IPaymentStore>(provider =>
                new CrashAfterCommitPaymentStore(provider.GetRequiredService<PaymentStore>(), crashSwitch));
        });

        await this.fixture.SendAsync(
            "evt_crash_succeeded",
            Event("evt_crash_succeeded", "payment_intent.succeeded", "succeeded", PaymentIntentId));
        await this.fixture.WaitUntilQueueIsEmptyAsync();
        await worker.StopAsync();

        Assert.Equal(1, crashSwitch.Crashes);
        Assert.Contains(this.logs.Messages, message => message.Contains("Event evt_crash_succeeded was already processed", StringComparison.Ordinal));
        await using PaymentsDbContext dbContext = this.fixture.CreateDbContext();
        Payment payment = await dbContext.Payments.SingleAsync(candidate => candidate.PaymentIntentId == PaymentIntentId);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(1, await dbContext.InboxEvents.CountAsync(inbox => inbox.PaymentIntentId == PaymentIntentId));
        Assert.Equal(1, await dbContext.PaymentHistory.CountAsync(entry => entry.PaymentIntentId == PaymentIntentId));
        Assert.Equal(0, await this.fixture.CountDeadLettersAsync());
    }

    private static string Event(string eventId, string type, string status, string paymentIntentId, string? demoFault = null)
    {
        string metadata = demoFault is null ? "{}" : $$"""{"demo_fault":"{{demoFault}}"}""";
        return $$"""
            {"id":"{{eventId}}","type":"{{type}}","created":1792181099,
             "data":{"object":{"id":"{{paymentIntentId}}","object":"payment_intent","amount":2000,"currency":"usd","status":"{{status}}","metadata":{{metadata}}} } }
            """;
    }

    private async Task<byte[]> RowVersionAsync(string paymentIntentId)
    {
        await using PaymentsDbContext dbContext = this.fixture.CreateDbContext();
        return await dbContext.Payments
            .Where(payment => payment.PaymentIntentId == paymentIntentId)
            .Select(payment => EF.Property<byte[]>(payment, "RowVersion"))
            .SingleAsync();
    }
}
