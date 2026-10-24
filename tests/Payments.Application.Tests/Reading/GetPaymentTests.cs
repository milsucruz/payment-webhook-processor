using Microsoft.Extensions.Logging;
using Payments.Application.Reading;
using Payments.Application.Tests.Webhooks;
using Payments.Domain;

namespace Payments.Application.Tests.Reading;

public class GetPaymentTests
{
    [Fact]
    public async Task Known_payment_is_found_with_its_history()
    {
        InMemoryPaymentReader reader = new();
        PaymentDetail detail = new(
            "pi_1",
            PaymentStatus.Paid,
            1000,
            0,
            "brl",
            "evt_2",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            [new PaymentHistoryItem("evt_2", "payment_intent.succeeded", null, PaymentStatus.Paid, true, null, DateTimeOffset.UnixEpoch, null)]);
        reader.Details.Add(detail);
        CollectingLogger<GetPayment> logger = new();
        GetPayment getPayment = new(reader, logger);

        GetPaymentResult result = await getPayment.ExecuteAsync("pi_1", CancellationToken.None);

        Assert.Equal(GetPaymentOutcome.Found, result.Outcome);
        Assert.Same(detail, result.Payment);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Unknown_payment_is_not_found_and_logged_as_information()
    {
        CollectingLogger<GetPayment> logger = new();
        GetPayment getPayment = new(new InMemoryPaymentReader(), logger);

        GetPaymentResult result = await getPayment.ExecuteAsync("pi_unknown", CancellationToken.None);

        Assert.Equal(GetPaymentOutcome.NotFound, result.Outcome);
        Assert.Null(result.Payment);
        Assert.Equal([LogLevel.Information], logger.Levels);
    }
}
