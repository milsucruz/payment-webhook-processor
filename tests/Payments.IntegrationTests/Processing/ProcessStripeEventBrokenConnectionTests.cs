using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Payments.Application.Processing;
using Payments.Domain;
using Payments.Infrastructure.Faults;
using Payments.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace Payments.IntegrationTests.Processing;

public class ProcessStripeEventBrokenConnectionTests : IAsyncLifetime
{
    private const string PaymentIntentId = "pi_broken_connection";
    private const string EventId = "evt_broken_connection";
    private const string Payload = $$"""
        {"id":"{{EventId}}","type":"payment_intent.succeeded","created":1792181099,
         "data":{"object":{"id":"{{PaymentIntentId}}","object":"payment_intent","amount":2000,"currency":"usd","status":"succeeded"} } }
        """;

    private readonly MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04").Build();

    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await this.container.StartAsync();
        SqlConnectionStringBuilder builder = new(this.container.GetConnectionString())
        {
            InitialCatalog = "Payments",
            CommandTimeout = 3,
        };
        this.connectionString = builder.ConnectionString;

        await using PaymentsDbContext dbContext = this.CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (this.container.State == TestcontainersStates.Paused)
        {
            await this.container.UnpauseAsync();
        }

        await this.container.DisposeAsync();
    }

    [Fact]
    public async Task Transient_error_from_a_paused_database_is_retried_and_a_later_attempt_commits()
    {
        await this.ProcessWithPauseAndAssert(rollBackOnDispose: false, expectedRollbackFailures: 0);
    }

    [Fact]
    public async Task Retry_still_happens_when_the_rollback_of_the_broken_transaction_throws()
    {
        await this.ProcessWithPauseAndAssert(rollBackOnDispose: true, expectedRollbackFailures: 1);
    }

    private async Task ProcessWithPauseAndAssert(bool rollBackOnDispose, int expectedRollbackFailures)
    {
        await using PaymentsDbContext processingContext = this.CreateDbContext();
        PausingPaymentStore store = new(processingContext, this.container, rollBackOnDispose);
        ProcessStripeEvent processStripeEvent = new(store, new SqlTransientErrorDetector(), new NoFaultInjector(), new DiscardingNotificationPublisher(), TimeProvider.System, NullLogger<ProcessStripeEvent>.Instance);

        ProcessStripeEventResult result = await processStripeEvent.ExecuteAsync(Payload, CancellationToken.None);

        Assert.Equal(ProcessStripeEventOutcome.Applied, result.Outcome);
        Assert.Equal(2, store.Transactions);
        Assert.Equal(expectedRollbackFailures, store.RollbackFailuresWithTransactionLeftOpen);

        await using PaymentsDbContext dbContext = this.CreateDbContext();
        Payment payment = await dbContext.Payments.SingleAsync(payment => payment.PaymentIntentId == PaymentIntentId);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(EventId, payment.LastEventId);
        Assert.Equal(1, await dbContext.InboxEvents.CountAsync(inboxEvent => inboxEvent.EventId == EventId));
        Assert.Equal(1, await dbContext.PaymentHistory.CountAsync(entry => entry.EventId == EventId));
    }

    private PaymentsDbContext CreateDbContext()
    {
        DbContextOptions<PaymentsDbContext> options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseSqlServer(this.connectionString)
            .Options;
        return new PaymentsDbContext(options);
    }
}
