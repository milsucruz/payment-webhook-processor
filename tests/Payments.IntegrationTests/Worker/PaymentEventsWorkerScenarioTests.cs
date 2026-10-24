using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    public Task InitializeAsync() => this.fixture.PurgeAsync(CancellationToken.None);

    public Task DisposeAsync() => this.fixture.StopWorkersAsync(CancellationToken.None);

    [Fact]
    public async Task Duplicate_delivery_writes_one_row_and_rolls_back()
    {
        const string PaymentIntentId = "pi_duplicate";
        string succeeded = Event("evt_duplicate_succeeded", "payment_intent.succeeded", "succeeded", PaymentIntentId);
        await this.fixture.StartWorkerAsync(this.logs, faultInjection: false, overrideServices: null, CancellationToken.None);

        await this.fixture.SendAsync("evt_duplicate_succeeded", succeeded, CancellationToken.None);
        await this.fixture.WaitUntilQueueIsEmptyAsync(CancellationToken.None);
        byte[] rowVersionBefore = await this.RowVersionAsync(PaymentIntentId);
        await this.fixture.SendAsync("evt_duplicate_succeeded", succeeded, CancellationToken.None);
        await this.fixture.WaitUntilQueueIsEmptyAsync(CancellationToken.None);
        await this.fixture.StopWorkersAsync(CancellationToken.None);

        await using PaymentsDbContext dbContext = this.fixture.CreateDbContext();
        Payment payment = await dbContext.Payments.SingleAsync(candidate => candidate.PaymentIntentId == PaymentIntentId);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(1, await dbContext.InboxEvents.CountAsync(inbox => inbox.PaymentIntentId == PaymentIntentId));
        Assert.Equal(1, await dbContext.PaymentHistory.CountAsync(entry => entry.PaymentIntentId == PaymentIntentId));
        Assert.Equal(rowVersionBefore, await this.RowVersionAsync(PaymentIntentId));
        Assert.Contains(this.logs.Messages, message => message.Contains("Event evt_duplicate_succeeded was already processed", StringComparison.Ordinal));
        Assert.Equal(0, await this.fixture.CountDeadLettersAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Poison_message_reaches_the_dead_letter_after_5_deliveries()
    {
        const string PaymentIntentId = "pi_poison";
        await this.fixture.StartWorkerAsync(this.logs, faultInjection: true, overrideServices: null, CancellationToken.None);

        await this.fixture.SendAsync(
            "evt_poison_succeeded",
            Event("evt_poison_succeeded", "payment_intent.succeeded", "succeeded", PaymentIntentId, demoFault: "poison"),
            CancellationToken.None);
        ServiceBusReceivedMessage deadLettered = await this.fixture.PeekDeadLetterAsync(CancellationToken.None);
        await this.fixture.StopWorkersAsync(CancellationToken.None);

        Assert.Equal("evt_poison_succeeded", deadLettered.MessageId);
        Assert.Equal("MaxDeliveryCountExceeded", deadLettered.DeadLetterReason);
        Assert.Equal(5, deadLettered.DeliveryCount);
        Assert.False(deadLettered.ApplicationProperties.ContainsKey("LastFailureReason"));
        await using PaymentsDbContext dbContext = this.fixture.CreateDbContext();
        Assert.False(await dbContext.Payments.AnyAsync(payment => payment.PaymentIntentId == PaymentIntentId));
        Assert.False(await dbContext.InboxEvents.AnyAsync(inbox => inbox.PaymentIntentId == PaymentIntentId));
    }

    [Fact]
    public async Task Abandoned_message_reaches_the_dead_letter_with_the_last_failure_reason()
    {
        const string PaymentIntentId = "pi_always_conflicting";
        await this.fixture.StartWorkerAsync(
            this.logs,
            faultInjection: false,
            overrideServices: services =>
            {
                services.AddScoped<PaymentStore>();
                services.AddScoped<IPaymentStore>(provider => new ConflictingPaymentStore(provider.GetRequiredService<PaymentStore>()));
            },
            CancellationToken.None);

        await this.fixture.SendAsync(
            "evt_always_conflicting",
            Event("evt_always_conflicting", "payment_intent.succeeded", "succeeded", PaymentIntentId),
            CancellationToken.None);
        ServiceBusReceivedMessage deadLettered = await this.fixture.PeekDeadLetterAsync(CancellationToken.None);
        await this.fixture.StopWorkersAsync(CancellationToken.None);

        Assert.Equal("evt_always_conflicting", deadLettered.MessageId);
        Assert.Equal("MaxDeliveryCountExceeded", deadLettered.DeadLetterReason);
        Assert.Equal(5, deadLettered.DeliveryCount);
        string lastFailureReason = Assert.IsType<string>(deadLettered.ApplicationProperties["LastFailureReason"]);
        Assert.Contains("evt_always_conflicting", lastFailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Impossible_conflict_reaches_the_dead_letter_on_the_first_delivery()
    {
        const string PaymentIntentId = "pi_conflict";
        await this.fixture.StartWorkerAsync(this.logs, faultInjection: false, overrideServices: null, CancellationToken.None);

        await this.fixture.SendAsync(
            "evt_conflict_canceled",
            Event("evt_conflict_canceled", "payment_intent.canceled", "canceled", PaymentIntentId),
            CancellationToken.None);
        await this.fixture.WaitUntilQueueIsEmptyAsync(CancellationToken.None);
        await this.fixture.SendAsync(
            "evt_conflict_succeeded",
            Event("evt_conflict_succeeded", "payment_intent.succeeded", "succeeded", PaymentIntentId),
            CancellationToken.None);
        ServiceBusReceivedMessage deadLettered = await this.fixture.PeekDeadLetterAsync(CancellationToken.None);
        await this.fixture.StopWorkersAsync(CancellationToken.None);

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
        await this.fixture.StartWorkerAsync(
            this.logs,
            faultInjection: false,
            overrideServices: services =>
            {
                services.AddScoped<PaymentStore>();
                services.AddScoped<IPaymentStore>(provider =>
                    new CrashAfterCommitPaymentStore(provider.GetRequiredService<PaymentStore>(), crashSwitch));
            },
            CancellationToken.None);

        await this.fixture.SendAsync(
            "evt_crash_succeeded",
            Event("evt_crash_succeeded", "payment_intent.succeeded", "succeeded", PaymentIntentId),
            CancellationToken.None);
        await this.fixture.WaitUntilQueueIsEmptyAsync(CancellationToken.None);
        await this.fixture.StopWorkersAsync(CancellationToken.None);

        Assert.True(crashSwitch.Crashed);
        Assert.Contains(this.logs.Messages, message => message.Contains("Event evt_crash_succeeded was already processed", StringComparison.Ordinal));
        await using PaymentsDbContext dbContext = this.fixture.CreateDbContext();
        Payment payment = await dbContext.Payments.SingleAsync(candidate => candidate.PaymentIntentId == PaymentIntentId);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(1, await dbContext.InboxEvents.CountAsync(inbox => inbox.PaymentIntentId == PaymentIntentId));
        Assert.Equal(1, await dbContext.PaymentHistory.CountAsync(entry => entry.PaymentIntentId == PaymentIntentId));
        Assert.Equal(0, await this.fixture.CountDeadLettersAsync(CancellationToken.None));
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
