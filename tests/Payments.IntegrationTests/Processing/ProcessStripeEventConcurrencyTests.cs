using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Payments.Application.Processing;
using Payments.Domain;
using Payments.Infrastructure.Faults;
using Payments.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace Payments.IntegrationTests.Processing;

public class ProcessStripeEventConcurrencyTests : IAsyncLifetime
{
    private const string PaymentIntentId = "pi_concurrency";

    private readonly MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04").Build();

    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await this.container.StartAsync();
        SqlConnectionStringBuilder builder = new(this.container.GetConnectionString())
        {
            InitialCatalog = "Payments",
        };
        this.connectionString = builder.ConnectionString;

        await using PaymentsDbContext dbContext = this.CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => this.container.DisposeAsync().AsTask();

    [Fact]
    public async Task Two_simultaneous_events_for_an_existing_payment_end_paid_with_two_history_rows()
    {
        await using (PaymentsDbContext dbContext = this.CreateDbContext())
        {
            DateTimeOffset createdAt = DateTimeOffset.UtcNow;
            dbContext.Payments.Add(new Payment(PaymentIntentId, PaymentStatus.Created, 2000, 0, "usd", "evt_created", createdAt, createdAt));
            await dbContext.SaveChangesAsync();
        }

        await this.ProcessSimultaneouslyAndAssert(expectedFromStatus: PaymentStatus.Created);
    }

    [Fact]
    public async Task Two_simultaneous_events_for_an_unknown_payment_end_paid_with_two_history_rows()
    {
        await this.ProcessSimultaneouslyAndAssert(expectedFromStatus: null);
    }

    private static string Event(string eventId, string type, string status)
    {
        return $$"""
            {"id":"{{eventId}}","type":"{{type}}","created":1792181099,
             "data":{"object":{"id":"{{PaymentIntentId}}","object":"payment_intent","amount":2000,"currency":"usd","status":"{{status}}"} } }
            """;
    }

    private async Task ProcessSimultaneouslyAndAssert(PaymentStatus? expectedFromStatus)
    {
        ReadBarrier barrier = new(participants: 2);
        await using PaymentsDbContext requiresActionContext = this.CreateDbContext();
        await using PaymentsDbContext succeededContext = this.CreateDbContext();
        BarrierPaymentStore requiresActionStore = new(new PaymentStore(requiresActionContext), barrier);
        BarrierPaymentStore succeededStore = new(new PaymentStore(succeededContext), barrier);

        Task<ProcessStripeEventResult> requiresAction = Task.Run(() => Process(
            requiresActionStore,
            Event("evt_requires_action", "payment_intent.requires_action", "requires_action")));
        Task<ProcessStripeEventResult> succeeded = Task.Run(() => Process(
            succeededStore,
            Event("evt_succeeded", "payment_intent.succeeded", "succeeded")));
        ProcessStripeEventResult[] results = await Task.WhenAll(requiresAction, succeeded);

        Assert.Equal(1, requiresActionStore.ConcurrencyConflicts + succeededStore.ConcurrencyConflicts);
        Assert.All(results, result => Assert.Contains(
            result.Outcome,
            new[] { ProcessStripeEventOutcome.Applied, ProcessStripeEventOutcome.IgnoredOutOfOrder }));

        await using PaymentsDbContext dbContext = this.CreateDbContext();
        Payment payment = await dbContext.Payments.SingleAsync();
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal("evt_succeeded", payment.LastEventId);
        Assert.Equal(2, await dbContext.InboxEvents.CountAsync());

        List<PaymentHistoryEntry> history = await dbContext.PaymentHistory.OrderBy(entry => entry.Id).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal(expectedFromStatus, history[0].FromStatus);
        Assert.True(history[0].Applied);
    }

    private static Task<ProcessStripeEventResult> Process(IPaymentStore store, string payload)
    {
        ProcessStripeEvent processStripeEvent = new(store, new SqlTransientErrorDetector(), new NoFaultInjector(), new DiscardingNotificationPublisher(), TimeProvider.System, NullLogger<ProcessStripeEvent>.Instance);
        return processStripeEvent.ExecuteAsync(payload, CancellationToken.None);
    }

    private PaymentsDbContext CreateDbContext()
    {
        DbContextOptions<PaymentsDbContext> options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseSqlServer(this.connectionString)
            .Options;
        return new PaymentsDbContext(options);
    }
}
