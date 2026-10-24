using System.Net;
using System.Text;
using System.Text.Json;

namespace Simulator;

public static class FloodMode
{
    public const int DefaultCount = 100;

    public const string DefaultFault = "slow";

    public static IReadOnlySet<string> Faults { get; } = new HashSet<string> { "slow", "transient", "poison" };

    public static TimeSpan SendInterval { get; } = TimeSpan.FromMilliseconds(125);

    public static string BuildEvent(string runId, int index, long created, string fault)
    {
        string suffix = $"flood_{runId}_{index:D4}";

        return JsonSerializer.Serialize(new
        {
            id = $"evt_{suffix}",
            @object = "event",
            type = "payment_intent.succeeded",
            created,
            livemode = false,
            data = new
            {
                @object = new
                {
                    id = $"pi_{suffix}",
                    @object = "payment_intent",
                    amount = PayMode.AmountInCents,
                    currency = PayMode.Currency,
                    status = "succeeded",
                    metadata = new Dictionary<string, string> { ["demo_fault"] = fault },
                },
            },
        });
    }

    public static async Task<int> RunAsync(
        HttpClient httpClient,
        Uri webhookUrl,
        string webhookSecret,
        int count,
        string fault,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        string runId = Guid.NewGuid().ToString("N")[..8];
        Console.WriteLine($"Flooding {webhookUrl} with {count} payments (run {runId}, demo_fault={fault}).");

        SortedDictionary<HttpStatusCode, int> responses = [];
        for (int index = 1; index <= count; index++)
        {
            if (index > 1)
            {
                await Task.Delay(SendInterval, timeProvider, cancellationToken);
            }

            long now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
            string payload = BuildEvent(runId, index, now, fault);
            using HttpRequestMessage request = new(HttpMethod.Post, webhookUrl)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("Stripe-Signature", StripeSignature.Header(payload, webhookSecret, now));

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            responses[response.StatusCode] = responses.GetValueOrDefault(response.StatusCode) + 1;
        }

        foreach (KeyValuePair<HttpStatusCode, int> response in responses)
        {
            Console.WriteLine($"{(int)response.Key} {response.Key}: {response.Value}");
        }

        return responses.Keys.All(statusCode => statusCode == HttpStatusCode.OK) ? 0 : 1;
    }
}
