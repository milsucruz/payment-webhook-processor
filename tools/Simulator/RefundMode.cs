using Stripe;

namespace Simulator;

// Details: docs/code/RefundMode.md
public static class RefundMode
{
    public static RefundCreateOptions BuildOptions(string paymentIntentId, long? amountInCents) => new()
    {
        PaymentIntent = paymentIntentId,
        Amount = amountInCents,
    };

    public static async Task<int> RunAsync(
        IStripeClient client,
        string paymentIntentId,
        long? amountInCents,
        CancellationToken cancellationToken)
    {
        RefundService service = new(client);
        Refund refund = await service.CreateAsync(BuildOptions(paymentIntentId, amountInCents), cancellationToken: cancellationToken);
        Console.WriteLine($"{refund.Id} {paymentIntentId} {refund.Amount} {refund.Currency} {refund.Status}");

        return 0;
    }
}
