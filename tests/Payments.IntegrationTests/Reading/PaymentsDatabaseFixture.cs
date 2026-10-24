using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Payments.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace Payments.IntegrationTests.Reading;

public sealed class PaymentsDatabaseFixture : IAsyncLifetime
{
    private readonly MsSqlContainer sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04").Build();

    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await this.sqlServer.StartAsync();
        SqlConnectionStringBuilder builder = new(this.sqlServer.GetConnectionString())
        {
            InitialCatalog = "Payments",
        };
        this.connectionString = builder.ConnectionString;
        await using PaymentsDbContext dbContext = this.CreateDbContext(null);
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => this.sqlServer.DisposeAsync().AsTask();

    public PaymentsDbContext CreateDbContext(List<string>? executedCommands)
    {
        DbContextOptionsBuilder<PaymentsDbContext> options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseSqlServer(this.connectionString);
        if (executedCommands is not null)
        {
            options.LogTo(executedCommands.Add, [RelationalEventId.CommandExecuted]);
        }

        return new PaymentsDbContext(options.Options);
    }

    public async Task ClearAsync()
    {
        await using PaymentsDbContext dbContext = this.CreateDbContext(null);
        await dbContext.PaymentHistory.ExecuteDeleteAsync();
        await dbContext.InboxEvents.ExecuteDeleteAsync();
        await dbContext.Payments.ExecuteDeleteAsync();
    }
}
