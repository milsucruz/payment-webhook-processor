using Stripe;

namespace Simulator;

// Details: docs/code/PayMode.md
public static class TestModeStripeClient
{
    public const string SecretKeySetting = "Stripe:SecretKey";

    public static bool IsTestModeKey(string apiKey) =>
        apiKey.StartsWith("sk_test_", StringComparison.Ordinal) || apiKey.StartsWith("rk_test_", StringComparison.Ordinal);

    public static StripeClient Create(string apiKey)
    {
        if (!IsTestModeKey(apiKey))
        {
            throw new InvalidOperationException($"{SecretKeySetting} must be a test mode key (sk_test_ or rk_test_).");
        }

        return new StripeClient(apiKey);
    }
}
