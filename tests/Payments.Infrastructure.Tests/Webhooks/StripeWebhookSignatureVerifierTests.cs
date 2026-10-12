using System.Text;
using Microsoft.Extensions.Options;
using Payments.Infrastructure.Webhooks;

namespace Payments.Infrastructure.Tests.Webhooks;

public class StripeWebhookSignatureVerifierTests
{
    private const string Secret = "whsec_test_secret";
    private const string Payload = """{"id":"evt_test_1","type":"payment_intent.succeeded","data":{"object":{"id":"pi_test_1","amount":2000}}}""";

    private static readonly DateTimeOffset now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private static StripeWebhookSignatureVerifier CreateVerifier() =>
        new(Options.Create(new StripeOptions { WebhookSecret = Secret }), new FixedTimeProvider(now));

    private static long SecondsAgo(long seconds) => now.ToUnixTimeSeconds() - seconds;

    [Fact]
    public void Valid_signature_is_accepted()
    {
        string header = StripeSignatureHelper.Header(Payload, Secret, SecondsAgo(0));

        SignatureVerificationResult result = CreateVerifier().Verify(Payload, header);

        Assert.True(result.IsValid);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Signature_made_with_another_secret_is_rejected()
    {
        string header = StripeSignatureHelper.Header(Payload, "whsec_other_secret", SecondsAgo(0));

        SignatureVerificationResult result = CreateVerifier().Verify(Payload, header);

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public void Body_with_one_byte_changed_is_rejected()
    {
        string header = StripeSignatureHelper.Header(Payload, Secret, SecondsAgo(0));
        byte[] bytes = Encoding.UTF8.GetBytes(Payload);
        bytes[bytes.Length / 2] ^= 0x01;
        string tampered = Encoding.UTF8.GetString(bytes);

        SignatureVerificationResult result = CreateVerifier().Verify(tampered, header);

        Assert.NotEqual(Payload, tampered);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Timestamp_at_the_tolerance_limit_is_accepted()
    {
        string header = StripeSignatureHelper.Header(
            Payload, Secret, SecondsAgo(StripeWebhookSignatureVerifier.ToleranceSeconds));

        SignatureVerificationResult result = CreateVerifier().Verify(Payload, header);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Timestamp_older_than_the_tolerance_is_rejected()
    {
        string header = StripeSignatureHelper.Header(
            Payload, Secret, SecondsAgo(StripeWebhookSignatureVerifier.ToleranceSeconds + 1));

        SignatureVerificationResult result = CreateVerifier().Verify(Payload, header);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_header_is_rejected(string? header)
    {
        SignatureVerificationResult result = CreateVerifier().Verify(Payload, header);

        Assert.False(result.IsValid);
        Assert.Equal("Missing Stripe-Signature header.", result.FailureReason);
    }

    [Theory]
    [InlineData("not-a-signature")]
    [InlineData("t=abc,v1=123")]
    [InlineData("v1=abc")]
    public void Malformed_header_is_rejected(string header)
    {
        SignatureVerificationResult result = CreateVerifier().Verify(Payload, header);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Header_with_two_signatures_is_accepted_when_one_matches()
    {
        long timestamp = SecondsAgo(0);
        string header = $"t={timestamp},"
            + $"v1={StripeSignatureHelper.ComputeV1(Payload, "whsec_old_secret", timestamp)},"
            + $"v1={StripeSignatureHelper.ComputeV1(Payload, Secret, timestamp)}";

        SignatureVerificationResult result = CreateVerifier().Verify(Payload, header);

        Assert.True(result.IsValid);
    }
}
