using System.Text.Json;
using System.Text.Json.Serialization;

namespace Payments.Application.Notifications;

public static class PaymentNotificationJson
{
    private static readonly JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(PaymentNotification notification) => JsonSerializer.Serialize(notification, options);

    public static PaymentNotification Deserialize(string json) =>
        JsonSerializer.Deserialize<PaymentNotification>(json, options)
        ?? throw new JsonException("The payment notification is null.");
}
