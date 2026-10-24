using System.Text.Json;
using Payments.Application.Notifications;
using Payments.Application.Processing;
using Payments.Application.Reading;
using Payments.Domain;

namespace Payments.Application.Tests.Notifications;

public class PaymentNotificationJsonTests
{
    [Fact]
    public void Notification_is_written_with_enums_as_strings_and_read_back_unchanged()
    {
        PaymentNotification notification = new(
            "evt_1",
            "charge.refunded",
            "pi_1",
            ProcessStripeEventOutcome.Applied,
            new PaymentSummary("pi_1", PaymentStatus.PartiallyRefunded, 2000, 500, "brl", new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero)));

        string json = PaymentNotificationJson.Serialize(notification);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal("Applied", document.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("PartiallyRefunded", document.RootElement.GetProperty("payment").GetProperty("status").GetString());
        Assert.Equal(notification, PaymentNotificationJson.Deserialize(json));
    }

    [Fact]
    public void Notification_without_a_payment_is_read_back_with_a_null_payment()
    {
        PaymentNotification notification = new("evt_1", "payment_intent.succeeded", "pi_1", ProcessStripeEventOutcome.Duplicate, null);

        PaymentNotification read = PaymentNotificationJson.Deserialize(PaymentNotificationJson.Serialize(notification));

        Assert.Equal(notification, read);
    }
}
