using System.Security.Cryptography;
using System.Text;

namespace Simulator;

// Details: docs/code/ReplayMode.md
public static class StripeSignature
{
    public static string Header(string payload, string secret, long timestamp)
    {
        byte[] hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));

        return $"t={timestamp},v1={Convert.ToHexStringLower(hash)}";
    }
}
