using System.Security.Cryptography;
using System.Text;

namespace Payments.Infrastructure.Tests.Webhooks;

// Builds Stripe-Signature values from Stripe's published scheme, independently of Stripe.net:
// v1 = hex(HMAC-SHA256(secret, "{timestamp}.{payload}")).
public static class StripeSignatureHelper
{
    public static string ComputeV1(string payload, string secret, long timestamp)
    {
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));

        return Convert.ToHexStringLower(hash);
    }

    public static string Header(string payload, string secret, long timestamp) =>
        $"t={timestamp},v1={ComputeV1(payload, secret, timestamp)}";
}
