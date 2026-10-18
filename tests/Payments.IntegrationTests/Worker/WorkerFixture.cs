using Azure.Messaging.ServiceBus;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Payments.Infrastructure.Persistence;
using Testcontainers.MsSql;
using Testcontainers.ServiceBus;

namespace Payments.IntegrationTests.Worker;

// Details: docs/code/WorkerFixture.md
public sealed class WorkerFixture : IAsyncLifetime
{
    public const string QueueName = "payment-events";

    private const string SqlServerAlias = "sqlserver";
    private const string SqlServerPassword = "Integration-Tests-1";

    private readonly INetwork network;
    private readonly MsSqlContainer sqlServer;
    private readonly ServiceBusContainer serviceBus;

    public WorkerFixture()
    {
        this.network = new NetworkBuilder().Build();
        this.sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04")
            .WithPassword(SqlServerPassword)
            .WithNetwork(this.network)
            .WithNetworkAliases(SqlServerAlias)
            .Build();
        this.serviceBus = new ServiceBusBuilder("mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.1")
            .WithAcceptLicenseAgreement(true)
            .WithConfig(Path.Combine(AppContext.BaseDirectory, "servicebus-emulator-config.json"))
            .WithMsSqlContainer(this.network, this.sqlServer, SqlServerAlias, SqlServerPassword)
            .Build();
    }

    public ServiceBusClient Client { get; private set; } = null!;

    public string PaymentsConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await this.network.CreateAsync();
        await this.sqlServer.StartAsync();
        await this.serviceBus.StartAsync();

        SqlConnectionStringBuilder builder = new(this.sqlServer.GetConnectionString())
        {
            InitialCatalog = "Payments",
        };
        this.PaymentsConnectionString = builder.ConnectionString;
        await using PaymentsDbContext dbContext = this.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        this.Client = new ServiceBusClient(this.serviceBus.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await this.Client.DisposeAsync();
        await this.serviceBus.DisposeAsync();
        await this.sqlServer.DisposeAsync();
        await this.network.DisposeAsync();
    }

    public PaymentsDbContext CreateDbContext()
    {
        DbContextOptions<PaymentsDbContext> options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseSqlServer(this.PaymentsConnectionString)
            .Options;
        return new PaymentsDbContext(options);
    }

    public async Task<IHost> StartWorkerAsync(
        ILoggerProvider loggerProvider,
        bool faultInjection = false,
        Action<IServiceCollection>? overrideServices = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceBus:ConnectionString"] = this.serviceBus.GetConnectionString(),
            ["ServiceBus:EventsQueueName"] = QueueName,
            ["ConnectionStrings:Payments"] = this.PaymentsConnectionString,
            ["FaultInjection:Enabled"] = faultInjection ? "true" : "false",
        });
        builder.Logging.AddProvider(loggerProvider);

        Program.AddPaymentEventsWorker(builder.Services, builder.Configuration);
        overrideServices?.Invoke(builder.Services);

        IHost host = builder.Build();
        await host.StartAsync();
        return host;
    }

    public async Task SendAsync(string eventId, string body)
    {
        await using ServiceBusSender sender = this.Client.CreateSender(QueueName);
        await sender.SendMessageAsync(new ServiceBusMessage(body) { MessageId = eventId, ContentType = "application/json" });
    }

    public async Task WaitUntilQueueIsEmptyAsync()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(150));
        while (await this.PeekAsync(SubQueue.None) > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token);
        }
    }

    public async Task<ServiceBusReceivedMessage> PeekDeadLetterAsync()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(150));
        while (true)
        {
            await using ServiceBusReceiver receiver = this.Client.CreateReceiver(
                QueueName,
                new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
            ServiceBusReceivedMessage? message = await receiver.PeekMessageAsync();
            if (message is not null)
            {
                return message;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token);
        }
    }

    public Task<int> CountDeadLettersAsync() => this.PeekAsync(SubQueue.DeadLetter);

    public async Task PurgeAsync()
    {
        foreach (SubQueue subQueue in new[] { SubQueue.None, SubQueue.DeadLetter })
        {
            await using ServiceBusReceiver receiver = this.Client.CreateReceiver(
                QueueName,
                new ServiceBusReceiverOptions { SubQueue = subQueue, ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });
            while ((await receiver.ReceiveMessagesAsync(100, TimeSpan.FromSeconds(1))).Count > 0)
            {
            }
        }
    }

    private async Task<int> PeekAsync(SubQueue subQueue)
    {
        await using ServiceBusReceiver receiver = this.Client.CreateReceiver(
            QueueName,
            new ServiceBusReceiverOptions { SubQueue = subQueue });
        return (await receiver.PeekMessagesAsync(100)).Count;
    }
}
