using Stripe;

namespace Simulator;

// Details: docs/code/PayMode.md
public static class PayMode
{
    public const long AmountInCents = 2000;

    public const string Currency = "brl";

    public static IReadOnlyList<TestCard> Cards { get; } =
    [
        new("approve", "pm_card_visa"),
        new("decline", "pm_card_chargeDeclined"),
        new("authenticate", "pm_card_authenticationRequired"),
    ];

    public static PaymentIntentCreateOptions BuildOptions(TestCard card) => new()
    {
        Amount = AmountInCents,
        Currency = Currency,
        PaymentMethod = card.PaymentMethod,
        Confirm = true,
        AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
        {
            Enabled = true,
            AllowRedirects = "never",
        },
    };

    public static async Task<int> RunAsync(IStripeClient client, CancellationToken cancellationToken)
    {
        PaymentIntentService service = new(client);

        foreach (TestCard card in Cards)
        {
            try
            {
                PaymentIntent paymentIntent = await service.CreateAsync(BuildOptions(card), cancellationToken: cancellationToken);
                Console.WriteLine($"{card.Name,-12} {paymentIntent.Id} {paymentIntent.Status}");
            }
            catch (StripeException exception) when (exception.StripeError?.PaymentIntent is not null)
            {
                PaymentIntent paymentIntent = exception.StripeError.PaymentIntent;
                Console.WriteLine($"{card.Name,-12} {paymentIntent.Id} {paymentIntent.Status} ({exception.StripeError.Code})");
            }
        }

        return 0;
    }
}
