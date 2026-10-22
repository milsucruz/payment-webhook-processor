using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Payments.Infrastructure.Persistence;
using Simulator;
using Stripe;

IConfiguration configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables()
    .Build();

using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

string mode = args.Length > 0 ? args[0] : string.Empty;

switch (mode)
{
    case "pay" when args.Length == 1:
        {
            StripeClient client = TestModeStripeClient.Create(Settings.Require(configuration, TestModeStripeClient.SecretKeySetting));
            return await PayMode.RunAsync(client, cancellation.Token);
        }

    case "refund" when args.Length is 2 or 3:
        {
            long? amountInCents = null;
            if (args.Length == 3)
            {
                if (!long.TryParse(args[2], out long parsedAmount) || parsedAmount <= 0)
                {
                    Console.Error.WriteLine("The amount must be a positive number of cents.");
                    return 2;
                }

                amountInCents = parsedAmount;
            }

            StripeClient client = TestModeStripeClient.Create(Settings.Require(configuration, TestModeStripeClient.SecretKeySetting));
            return await RefundMode.RunAsync(client, args[1], amountInCents, cancellation.Token);
        }

    case "replay" when args.Length is 1 or 2:
        {
            int seed = Random.Shared.Next();
            if (args.Length == 2 && !int.TryParse(args[1], out seed))
            {
                Console.Error.WriteLine("The seed must be an integer.");
                return 2;
            }

            DbContextOptions<PaymentsDbContext> options = new DbContextOptionsBuilder<PaymentsDbContext>()
                .UseSqlServer(Settings.Require(configuration, "ConnectionStrings:Payments"))
                .Options;
            await using PaymentsDbContext dbContext = new(options);
            using HttpClient httpClient = new();
            Uri webhookUrl = new(configuration["Simulator:WebhookUrl"] ?? "http://localhost:8080/webhooks/stripe");

            return await ReplayMode.RunAsync(
                dbContext,
                httpClient,
                webhookUrl,
                Settings.Require(configuration, "Stripe:WebhookSecret"),
                seed,
                TimeProvider.System,
                cancellation.Token);
        }

    case "flood" when args.Length is 1 or 2:
        {
            int count = FloodMode.DefaultCount;
            if (args.Length == 2 && (!int.TryParse(args[1], out count) || count <= 0))
            {
                Console.Error.WriteLine("The count must be a positive number.");
                return 2;
            }

            using HttpClient httpClient = new();
            Uri webhookUrl = new(configuration["Simulator:WebhookUrl"] ?? "http://localhost:8080/webhooks/stripe");

            return await FloodMode.RunAsync(
                httpClient,
                webhookUrl,
                Settings.Require(configuration, "Stripe:WebhookSecret"),
                count,
                TimeProvider.System,
                cancellation.Token);
        }

    default:
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  pay                               one payment per test card: approve, decline, authenticate");
        Console.Error.WriteLine("  refund <pi_...> [amount-in-cents] full refund, or partial with an amount");
        Console.Error.WriteLine("  replay [seed]                     resend every inbox event twice, shuffled, freshly signed");
        Console.Error.WriteLine("  flood [count]                     send count new paid payments (default 100) with demo_fault=slow");
        return 2;
}
