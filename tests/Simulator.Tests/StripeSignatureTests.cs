using Microsoft.Extensions.Options;
using Payments.Application.Webhooks;
using Payments.Infrastructure.Webhooks;

namespace Simulator.Tests;

public class StripeSignatureTests
{
    private const string Secret = "whsec_simulator_test_secret";

    private const string Payload = "{\"id\":\"evt_test\",\"object\":\"event\",\"data\":{\"object\":{\"id\":\"pi_test\",\"description\":\"café\"}}}";

    private static readonly DateTimeOffset now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Header_is_accepted_by_the_api_verifier_with_the_same_secret()
    {
        string header = StripeSignature.Header(Payload, Secret, now.ToUnixTimeSeconds());

        SignatureVerificationResult result = CreateVerifier(Secret).Verify(Payload, header);

        Assert.True(result.IsValid, result.FailureReason);
    }

    [Fact]
    public void Header_is_rejected_by_the_api_verifier_with_another_secret()
    {
        string header = StripeSignature.Header(Payload, Secret, now.ToUnixTimeSeconds());

        SignatureVerificationResult result = CreateVerifier("whsec_another_secret").Verify(Payload, header);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Header_carries_the_timestamp_it_was_signed_with()
    {
        string header = StripeSignature.Header(Payload, Secret, 1760702400);

        Assert.StartsWith("t=1760702400,v1=", header);
    }

    private static StripeWebhookSignatureVerifier CreateVerifier(string secret) =>
        new(Options.Create(new StripeOptions { WebhookSecret = secret }), new FixedTimeProvider(now));
}
