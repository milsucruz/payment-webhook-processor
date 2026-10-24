using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Payments.Application.Reading;
using Payments.Application.Tests.Reading;
using Payments.Domain;

namespace Payments.Api.Tests.Payments;

public class PaymentsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset start = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> factory;

    public PaymentsControllerTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task List_returns_the_first_page_newest_first_with_a_next_cursor()
    {
        InMemoryPaymentReader reader = new();
        reader.Payments.Add(new PaymentSummary("pi_1", PaymentStatus.Paid, 1000, 0, "brl", start));
        reader.Payments.Add(new PaymentSummary("pi_2", PaymentStatus.PartiallyRefunded, 2500, 500, "brl", start.AddSeconds(1)));
        HttpClient client = this.CreateClient(reader);

        HttpResponseMessage response = await client.GetAsync("/api/payments?limit=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("pi_2", item.GetProperty("paymentIntentId").GetString());
        Assert.Equal("PartiallyRefunded", item.GetProperty("status").GetString());
        Assert.Equal(2500, item.GetProperty("amount").GetInt64());
        Assert.Equal(500, item.GetProperty("amountRefunded").GetInt64());
        Assert.Equal("brl", item.GetProperty("currency").GetString());
        Assert.Equal(start.AddSeconds(1), item.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.NotNull(body.RootElement.GetProperty("nextCursor").GetString());
    }

    [Fact]
    public async Task List_with_the_next_cursor_returns_the_following_page()
    {
        InMemoryPaymentReader reader = new();
        reader.Payments.Add(new PaymentSummary("pi_1", PaymentStatus.Paid, 1000, 0, "brl", start));
        reader.Payments.Add(new PaymentSummary("pi_2", PaymentStatus.Paid, 1000, 0, "brl", start.AddSeconds(1)));
        HttpClient client = this.CreateClient(reader);
        using JsonDocument first = JsonDocument.Parse(await client.GetStringAsync("/api/payments?limit=1"));
        string cursor = first.RootElement.GetProperty("nextCursor").GetString()!;

        HttpResponseMessage response = await client.GetAsync($"/api/payments?limit=1&cursor={cursor}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("pi_1", item.GetProperty("paymentIntentId").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task List_without_a_limit_asks_for_the_default_page_size()
    {
        InMemoryPaymentReader reader = new();
        HttpClient client = this.CreateClient(reader);

        HttpResponseMessage response = await client.GetAsync("/api/payments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ListPayments.DefaultPageSize + 1, reader.RequestedCount);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("limit=abc")]
    [InlineData("cursor=not-a-cursor")]
    public async Task List_with_an_invalid_query_returns_400_and_reads_nothing(string query)
    {
        InMemoryPaymentReader reader = new();
        HttpClient client = this.CreateClient(reader);

        HttpResponseMessage response = await client.GetAsync($"/api/payments?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(reader.RequestedCount);
    }

    [Fact]
    public async Task Detail_returns_the_payment_with_its_history()
    {
        InMemoryPaymentReader reader = new();
        reader.Details.Add(new PaymentDetail(
            "pi_1",
            PaymentStatus.Paid,
            1000,
            0,
            "brl",
            "evt_2",
            start,
            start.AddSeconds(2),
            [
                new PaymentHistoryItem("evt_2", "payment_intent.succeeded", null, PaymentStatus.Paid, true, null, start, "0af7651916cd43dd8448eb211c80319c"),
                new PaymentHistoryItem("evt_1", "payment_intent.created", PaymentStatus.Paid, PaymentStatus.Created, false, "OutOfOrder", start.AddSeconds(2), null),
            ]));
        HttpClient client = this.CreateClient(reader);

        HttpResponseMessage response = await client.GetAsync("/api/payments/pi_1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Paid", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("evt_2", body.RootElement.GetProperty("lastEventId").GetString());
        JsonElement[] history = body.RootElement.GetProperty("history").EnumerateArray().ToArray();
        Assert.Equal(2, history.Length);
        Assert.Equal(JsonValueKind.Null, history[0].GetProperty("fromStatus").ValueKind);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", history[0].GetProperty("traceId").GetString());
        Assert.Equal("payment_intent.created", history[1].GetProperty("eventType").GetString());
        Assert.Equal("Paid", history[1].GetProperty("fromStatus").GetString());
        Assert.Equal("Created", history[1].GetProperty("toStatus").GetString());
        Assert.False(history[1].GetProperty("applied").GetBoolean());
        Assert.Equal("OutOfOrder", history[1].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Detail_of_an_unknown_payment_returns_404()
    {
        HttpClient client = this.CreateClient(new InMemoryPaymentReader());

        HttpResponseMessage response = await client.GetAsync("/api/payments/pi_unknown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient CreateClient(IPaymentReader reader) =>
        this.factory
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Stripe:WebhookSecret", "whsec_test_secret");
                builder.UseSetting("ServiceBus:ConnectionString", "Endpoint=sb://localhost;SharedAccessKeyName=test;SharedAccessKey=test");
                builder.UseSetting("ServiceBus:EventsQueueName", "payment-events");
                builder.UseSetting("ConnectionStrings:Payments", "Server=localhost;Database=Payments");
                builder.UseSetting("Admin:ApiKey", "test-admin-key");
                builder.ConfigureTestServices(services => services.AddSingleton(reader));
            })
            .CreateClient();
}
