using Payments.Application.Events;
using Payments.Domain;

namespace Payments.Application.Tests.Events;

public class StripeEventParserTests
{
    private static readonly string fixturesDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures");

    private static readonly string[] subscribedTypes =
    [
        "payment_intent.created",
        "payment_intent.processing",
        "payment_intent.requires_action",
        "payment_intent.succeeded",
        "payment_intent.payment_failed",
        "payment_intent.canceled",
        "charge.refunded",
    ];

    public static TheoryData<string, PaymentEvent> Fixtures => new()
    {
        {
            "payment_intent.created.json",
            new PaymentEvent("evt_3UPlWOKVj7nYvfcn1ypRRiVy", "payment_intent.created", At(1791820336), "pi_3UPlWOKVj7nYvfcn1pgPgiKK", PaymentStatus.Created, 2000, 0, "usd")
        },
        {
            "payment_intent.requires_action.json",
            new PaymentEvent("evt_3URHN9KVj7nYvfcn1bUOzT9a", "payment_intent.requires_action", At(1792181099), "pi_3URHN9KVj7nYvfcn14KD8W8T", PaymentStatus.RequiresAction, 2000, 0, "usd")
        },
        {
            "payment_intent.payment_failed.json",
            new PaymentEvent("evt_3URHNBKVj7nYvfcn0mZgn4xn", "payment_intent.payment_failed", At(1792181101), "pi_3URHNBKVj7nYvfcn05cBQpH0", PaymentStatus.Failed, 2000, 0, "usd")
        },
        {
            "payment_intent.succeeded.json",
            new PaymentEvent("evt_3UPlWOKVj7nYvfcn1VhC0nfc", "payment_intent.succeeded", At(1791820337), "pi_3UPlWOKVj7nYvfcn1pgPgiKK", PaymentStatus.Paid, 2000, 0, "usd")
        },
        {
            "payment_intent.canceled.json",
            new PaymentEvent("evt_3URHMoKVj7nYvfcn0mLLDXpb", "payment_intent.canceled", At(1792181079), "pi_3URHMoKVj7nYvfcn0HmzdGcs", PaymentStatus.Canceled, 2000, 0, "usd")
        },
        {
            "charge.refunded.partial.json",
            new PaymentEvent("evt_3URHNXKVj7nYvfcn0glpAsS1", "charge.refunded", At(1792181126), "pi_3URHNXKVj7nYvfcn0Tt1slXw", PaymentStatus.PartiallyRefunded, 2000, 500, "usd")
        },
        {
            "charge.refunded.json",
            new PaymentEvent("evt_3URHNDKVj7nYvfcn08ujeQIq", "charge.refunded", At(1792181105), "pi_3URHNDKVj7nYvfcn0DQWdTU0", PaymentStatus.Refunded, 100, 100, "usd")
        },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Real_Stripe_event_becomes_the_expected_payment_event(string fixture, PaymentEvent expected)
    {
        string payload = File.ReadAllText(Path.Combine(fixturesDirectory, fixture));

        PaymentEvent actual = StripeEventParser.Parse(payload);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Every_subscribed_type_except_processing_has_a_fixture()
    {
        string[] fixtureTypes = Directory.GetFiles(fixturesDirectory, "*.json")
            .Select(path => StripeEventParser.Parse(File.ReadAllText(path)).EventType)
            .Distinct()
            .Order()
            .ToArray();

        string[] expected = subscribedTypes.Where(type => type != "payment_intent.processing").Order().ToArray();
        Assert.Equal(expected, fixtureTypes);
    }

    [Fact]
    public void Processing_event_requests_the_processing_status()
    {
        string payload = """
            {"id":"evt_1","type":"payment_intent.processing","created":1792181099,
             "data":{"object":{"id":"pi_1","object":"payment_intent","amount":2000,"currency":"eur","status":"processing"}}}
            """;

        PaymentEvent actual = StripeEventParser.Parse(payload);

        Assert.Equal(new PaymentEvent("evt_1", "payment_intent.processing", At(1792181099), "pi_1", PaymentStatus.Processing, 2000, 0, "eur"), actual);
    }

    [Fact]
    public void Unknown_event_type_throws_a_permanent_error_naming_the_type()
    {
        string payload = """
            {"id":"evt_1","type":"payment_intent.amount_capturable_updated","created":1792181099,
             "data":{"object":{"id":"pi_1","amount":2000,"currency":"usd"}}}
            """;

        InvalidStripeEventException exception = Assert.Throws<InvalidStripeEventException>(() => StripeEventParser.Parse(payload));

        Assert.Contains("payment_intent.amount_capturable_updated", exception.Message);
    }

    [Fact]
    public void Body_that_is_not_json_throws_a_permanent_error()
    {
        Assert.Throws<InvalidStripeEventException>(() => StripeEventParser.Parse("not json"));
    }

    [Fact]
    public void Event_without_data_object_throws_a_permanent_error()
    {
        string payload = """{"id":"evt_1","type":"payment_intent.succeeded","created":1792181099,"data":{}}""";

        InvalidStripeEventException exception = Assert.Throws<InvalidStripeEventException>(() => StripeEventParser.Parse(payload));

        Assert.Contains("'object'", exception.Message);
    }

    [Fact]
    public void Refunded_charge_without_payment_intent_throws_a_permanent_error()
    {
        string payload = """
            {"id":"evt_1","type":"charge.refunded","created":1792181099,
             "data":{"object":{"id":"ch_1","object":"charge","amount":2000,"amount_refunded":2000,"currency":"usd","payment_intent":null}}}
            """;

        InvalidStripeEventException exception = Assert.Throws<InvalidStripeEventException>(() => StripeEventParser.Parse(payload));

        Assert.Contains("'payment_intent'", exception.Message);
    }

    [Theory]
    [InlineData("payment_intent.succeeded", """{"id":"pi_1","amount":2000,"currency":"usd","metadata":{"demo_fault":"poison"}}""")]
    [InlineData("charge.refunded", """{"id":"ch_1","amount":2000,"amount_refunded":2000,"currency":"usd","payment_intent":"pi_1","metadata":{"demo_fault":"poison"}}""")]
    public void Demo_fault_is_read_from_the_metadata_of_the_data_object(string eventType, string dataObject)
    {
        string payload = WrapEvent(eventType, dataObject);

        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);

        Assert.Equal("poison", paymentEvent.DemoFault);
    }

    [Theory]
    [InlineData("""{"id":"pi_1","amount":2000,"currency":"usd"}""")]
    [InlineData("""{"id":"pi_1","amount":2000,"currency":"usd","metadata":{}}""")]
    [InlineData("""{"id":"pi_1","amount":2000,"currency":"usd","metadata":null}""")]
    [InlineData("""{"id":"pi_1","amount":2000,"currency":"usd","metadata":{"demo_fault":1}}""")]
    public void Missing_or_non_string_demo_fault_is_null_and_not_an_error(string dataObject)
    {
        string payload = WrapEvent("payment_intent.succeeded", dataObject);

        PaymentEvent paymentEvent = StripeEventParser.Parse(payload);

        Assert.Null(paymentEvent.DemoFault);
    }

    private static string WrapEvent(string eventType, string dataObject)
    {
        return $$"""{"id":"evt_1","type":"{{eventType}}","created":1792181099,"data":{"object":""" + dataObject + "}}";
    }

    private static DateTimeOffset At(long unixSeconds)
    {
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
    }
}
