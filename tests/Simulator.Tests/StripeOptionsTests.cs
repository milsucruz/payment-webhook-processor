using Stripe;

namespace Simulator.Tests;

public class StripeOptionsTests
{
    [Fact]
    public void Pay_uses_one_card_that_approves_one_that_declines_and_one_that_requires_authentication()
    {
        Assert.Equal(
            ["pm_card_visa", "pm_card_chargeDeclined", "pm_card_authenticationRequired"],
            PayMode.Cards.Select(card => card.PaymentMethod));
    }

    [Fact]
    public void Pay_confirms_in_cents_with_automatic_payment_methods_and_no_redirects()
    {
        PaymentIntentCreateOptions options = PayMode.BuildOptions(new TestCard("approve", "pm_card_visa"));

        Assert.Equal(2000, options.Amount);
        Assert.Equal("brl", options.Currency);
        Assert.Equal("pm_card_visa", options.PaymentMethod);
        Assert.True(options.Confirm);
        Assert.True(options.AutomaticPaymentMethods.Enabled);
        Assert.Equal("never", options.AutomaticPaymentMethods.AllowRedirects);
    }

    [Fact]
    public void Refund_without_an_amount_is_full()
    {
        RefundCreateOptions options = RefundMode.BuildOptions("pi_test", null);

        Assert.Equal("pi_test", options.PaymentIntent);
        Assert.Null(options.Amount);
    }

    [Fact]
    public void Refund_with_an_amount_is_partial()
    {
        RefundCreateOptions options = RefundMode.BuildOptions("pi_test", 500);

        Assert.Equal(500, options.Amount);
    }

    [Theory]
    [InlineData("sk_test_abc", true)]
    [InlineData("rk_test_abc", true)]
    [InlineData("sk_live_abc", false)]
    [InlineData("rk_live_abc", false)]
    [InlineData("", false)]
    public void Only_test_mode_keys_are_accepted(string apiKey, bool accepted)
    {
        Assert.Equal(accepted, TestModeStripeClient.IsTestModeKey(apiKey));
    }
}
