using System.Net;
using Microsoft.Extensions.Options;
using Payments.Application.Events;
using Payments.Application.Webhooks;
using Payments.Domain;
using Payments.Infrastructure.Webhooks;

namespace Simulator.Tests;

public class FloodModeTests
{
    private const string Secret = "whsec_simulator_test_secret";

    private static readonly DateTimeOffset now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Event_is_a_paid_payment_with_the_slow_demo_fault_for_the_api_parser()
    {
        string payload = FloodMode.BuildEvent("run1", 7, now.ToUnixTimeSeconds());

        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);

        Assert.Equal("evt_flood_run1_0007", paymentEvent.EventId);
        Assert.Equal("payment_intent.succeeded", paymentEvent.EventType);
        Assert.Equal(now, paymentEvent.StripeCreatedAt);
        Assert.Equal("pi_flood_run1_0007", paymentEvent.PaymentIntentId);
        Assert.Equal(PaymentStatus.Paid, paymentEvent.RequestedStatus);
        Assert.Equal(PayMode.AmountInCents, paymentEvent.Amount);
        Assert.Equal(PayMode.Currency, paymentEvent.Currency);
        Assert.Equal("slow", paymentEvent.DemoFault);
    }

    [Fact]
    public async Task Run_sends_count_signed_deliveries_of_distinct_payments()
    {
        RecordingHandler handler = new(HttpStatusCode.OK);
        using HttpClient httpClient = new(handler);
        StripeWebhookSignatureVerifier verifier = new(Options.Create(new StripeOptions { WebhookSecret = Secret }), new FixedTimeProvider(now));

        int exitCode = await FloodMode.RunAsync(httpClient, new Uri("http://api/webhooks/stripe"), Secret, 3, new FixedTimeProvider(now), CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(3, handler.Deliveries.Count);
        Assert.All(handler.Deliveries, delivery => Assert.True(verifier.Verify(delivery.Payload, delivery.Signature).IsValid));
        Assert.Equal(3, handler.Deliveries.Select(delivery => StripeEventParser.Parse(delivery.Payload).PaymentIntentId).Distinct().Count());
    }

    [Fact]
    public async Task Run_exits_with_1_when_a_delivery_is_not_accepted()
    {
        RecordingHandler handler = new(HttpStatusCode.TooManyRequests);
        using HttpClient httpClient = new(handler);

        int exitCode = await FloodMode.RunAsync(httpClient, new Uri("http://api/webhooks/stripe"), Secret, 1, new FixedTimeProvider(now), CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    private sealed record Delivery(string Payload, string Signature);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode statusCode;

        public RecordingHandler(HttpStatusCode statusCode)
        {
            this.statusCode = statusCode;
        }

        public List<Delivery> Deliveries { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            this.Deliveries.Add(new Delivery(payload, request.Headers.GetValues("Stripe-Signature").Single()));

            return new HttpResponseMessage(this.statusCode);
        }
    }
}
