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
    public const string NotificationsQueueName = "payment-notifications";

    private const string SqlServerAlias = "sqlserver";
    private const string SqlServerPassword = "Integration-Tests-1";

    private static readonly TimeSpan waitTimeout = TimeSpan.FromSeconds(150);
    private static readonly TimeSpan pollInterval = TimeSpan.FromMilliseconds(200);

    private readonly INetwork network;
    private readonly MsSqlContainer sqlServer;
    private readonly ServiceBusContainer serviceBus;
    private readonly List<IHost> workers = [];

    private ServiceBusClient client = null!;
    private ServiceBusSender sender = null!;
    private ServiceBusReceiver queuePeeker = null!;
    private ServiceBusReceiver deadLetterPeeker = null!;

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

    public string PaymentsConnectionString { get; private set; } = string.Empty;

    public string ServiceBusConnectionString => this.serviceBus.GetConnectionString();

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

        this.client = new ServiceBusClient(this.serviceBus.GetConnectionString());
        this.sender = this.client.CreateSender(QueueName);
        this.queuePeeker = this.client.CreateReceiver(QueueName);
        this.deadLetterPeeker = this.client.CreateReceiver(QueueName, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
    }

    public async Task DisposeAsync()
    {
        await this.StopWorkersAsync(CancellationToken.None);
        await this.deadLetterPeeker.DisposeAsync();
        await this.queuePeeker.DisposeAsync();
        await this.sender.DisposeAsync();
        await this.client.DisposeAsync();
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

    public async Task StartWorkerAsync(
        ILoggerProvider? loggerProvider,
        bool faultInjection,
        Action<IServiceCollection>? overrideServices,
        CancellationToken cancellationToken)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceBus:ConnectionString"] = this.serviceBus.GetConnectionString(),
            ["ServiceBus:EventsQueueName"] = QueueName,
            ["ServiceBus:NotificationsQueueName"] = NotificationsQueueName,
            ["ConnectionStrings:Payments"] = this.PaymentsConnectionString,
            ["FaultInjection:Enabled"] = faultInjection ? "true" : "false",
        });
        if (loggerProvider is not null)
        {
            builder.Logging.AddProvider(loggerProvider);
        }

        Program.AddPaymentEventsWorker(builder.Services, builder.Configuration);
        overrideServices?.Invoke(builder.Services);

        IHost host = builder.Build();
        this.workers.Add(host);
        await host.StartAsync(cancellationToken);
    }

    public async Task StopWorkersAsync(CancellationToken cancellationToken)
    {
        foreach (IHost worker in this.workers)
        {
            await worker.StopAsync(cancellationToken);
            worker.Dispose();
        }

        this.workers.Clear();
    }

    public Task SendAsync(string eventId, string body, CancellationToken cancellationToken) =>
        this.sender.SendMessageAsync(
            new ServiceBusMessage(body) { MessageId = eventId, ContentType = "application/json" },
            cancellationToken);

    public async Task WaitUntilQueueIsEmptyAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(waitTimeout);
        while (await CountAsync(this.queuePeeker, timeout.Token) > 0)
        {
            await Task.Delay(pollInterval, timeout.Token);
        }
    }

    public async Task<ServiceBusReceivedMessage> PeekDeadLetterAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(waitTimeout);
        while (true)
        {
            ServiceBusReceivedMessage? message = await this.deadLetterPeeker.PeekMessageAsync(fromSequenceNumber: 0, timeout.Token);
            if (message is not null)
            {
                return message;
            }

            await Task.Delay(pollInterval, timeout.Token);
        }
    }

    public Task<int> CountDeadLettersAsync(CancellationToken cancellationToken) => CountAsync(this.deadLetterPeeker, cancellationToken);

    public async Task<ServiceBusReceivedMessage> ReceiveNotificationAsync(string eventId, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(waitTimeout);
        await using ServiceBusReceiver receiver = this.client.CreateReceiver(
            NotificationsQueueName,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });
        while (true)
        {
            ServiceBusReceivedMessage? message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(1), timeout.Token);
            if (message?.MessageId == eventId)
            {
                return message;
            }
        }
    }

    public async Task PurgeAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(waitTimeout);
        foreach (SubQueue subQueue in new[] { SubQueue.None, SubQueue.DeadLetter })
        {
            await using ServiceBusReceiver receiver = this.client.CreateReceiver(
                QueueName,
                new ServiceBusReceiverOptions { SubQueue = subQueue, ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });
            ServiceBusReceiver peeker = subQueue == SubQueue.None ? this.queuePeeker : this.deadLetterPeeker;
            while (await CountAsync(peeker, timeout.Token) > 0)
            {
                await receiver.ReceiveMessagesAsync(100, TimeSpan.FromSeconds(1), timeout.Token);
            }
        }
    }

    private static async Task<int> CountAsync(ServiceBusReceiver peeker, CancellationToken cancellationToken) =>
        (await peeker.PeekMessagesAsync(100, fromSequenceNumber: 0, cancellationToken)).Count;
}
