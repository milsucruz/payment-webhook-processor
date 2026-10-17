using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Payments.Infrastructure.Persistence;

namespace Simulator;

// Details: docs/code/ReplayMode.md
public static class ReplayMode
{
    public static async Task<int> RunAsync(
        PaymentsDbContext dbContext,
        HttpClient httpClient,
        Uri webhookUrl,
        string webhookSecret,
        int seed,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        List<string> payloads = await dbContext.InboxEvents
            .AsNoTracking()
            .OrderBy(inboxEvent => inboxEvent.EventId)
            .Select(inboxEvent => inboxEvent.Payload)
            .ToListAsync(cancellationToken);

        string[] deliveries = ReplayPlan.Build(payloads, new Random(seed));
        Console.WriteLine($"Replaying {payloads.Count} inbox events as {deliveries.Length} deliveries to {webhookUrl} (seed {seed}).");

        SortedDictionary<HttpStatusCode, int> responses = [];
        foreach (string payload in deliveries)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, webhookUrl)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation(
                "Stripe-Signature",
                StripeSignature.Header(payload, webhookSecret, timeProvider.GetUtcNow().ToUnixTimeSeconds()));

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
