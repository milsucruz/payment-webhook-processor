using System.Text.Json;
using Payments.Domain;

namespace Payments.Application.Events;

// Details: docs/code/StripeEventParser.md
public static class StripeEventParser
{
    public static PaymentEvent Parse(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            return Read(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidStripeEventException("The event body is not valid JSON.", exception);
        }
    }

    private static PaymentEvent Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidStripeEventException("The event body is not a JSON object.");
        }

        string eventId = ReadString(root, "id");
        string eventType = ReadString(root, "type");
        DateTimeOffset createdAt = DateTimeOffset.FromUnixTimeSeconds(ReadLong(root, "created"));
        JsonElement dataObject = ReadObject(ReadObject(root, "data"), "object");

        return eventType switch
        {
            "payment_intent.created" => FromPaymentIntent(eventId, eventType, createdAt, dataObject, PaymentStatus.Created),
            "payment_intent.requires_action" => FromPaymentIntent(eventId, eventType, createdAt, dataObject, PaymentStatus.RequiresAction),
            "payment_intent.processing" => FromPaymentIntent(eventId, eventType, createdAt, dataObject, PaymentStatus.Processing),
            "payment_intent.payment_failed" => FromPaymentIntent(eventId, eventType, createdAt, dataObject, PaymentStatus.Failed),
            "payment_intent.succeeded" => FromPaymentIntent(eventId, eventType, createdAt, dataObject, PaymentStatus.Paid),
            "payment_intent.canceled" => FromPaymentIntent(eventId, eventType, createdAt, dataObject, PaymentStatus.Canceled),
            "charge.refunded" => FromRefundedCharge(eventId, eventType, createdAt, dataObject),
            _ => throw new InvalidStripeEventException($"Event {eventId} has the unsupported type '{eventType}'."),
        };
    }

    private static PaymentEvent FromPaymentIntent(
        string eventId,
        string eventType,
        DateTimeOffset createdAt,
        JsonElement paymentIntent,
        PaymentStatus requestedStatus)
    {
        return new PaymentEvent(
            eventId,
            eventType,
            createdAt,
            ReadString(paymentIntent, "id"),
            requestedStatus,
            ReadLong(paymentIntent, "amount"),
            0,
            ReadString(paymentIntent, "currency"));
    }

    private static PaymentEvent FromRefundedCharge(
        string eventId,
        string eventType,
        DateTimeOffset createdAt,
        JsonElement charge)
    {
        long amount = ReadLong(charge, "amount");
        long amountRefunded = ReadLong(charge, "amount_refunded");
        PaymentStatus requestedStatus = amountRefunded < amount
            ? PaymentStatus.PartiallyRefunded
            : PaymentStatus.Refunded;

        return new PaymentEvent(
            eventId,
            eventType,
            createdAt,
            ReadString(charge, "payment_intent"),
            requestedStatus,
            amount,
            amountRefunded,
            ReadString(charge, "currency"));
    }

    private static JsonElement ReadObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement element) || element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidStripeEventException($"The field '{name}' is missing or is not an object.");
        }

        return element;
    }

    private static string ReadString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement element)
            || element.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new InvalidStripeEventException($"The field '{name}' is missing or is not a non-empty string.");
        }

        return element.GetString()!;
    }

    private static long ReadLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt64(out long value))
        {
            throw new InvalidStripeEventException($"The field '{name}' is missing or is not an integer.");
        }

        return value;
    }
}
