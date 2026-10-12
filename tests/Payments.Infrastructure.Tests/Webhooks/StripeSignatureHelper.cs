using System.Security.Cryptography;
using System.Text;

namespace Payments.Infrastructure.Tests.Webhooks;

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
