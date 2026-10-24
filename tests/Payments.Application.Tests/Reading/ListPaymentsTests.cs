using Microsoft.Extensions.Logging;
using Payments.Application.Reading;
using Payments.Application.Tests.Webhooks;
using Payments.Domain;

namespace Payments.Application.Tests.Reading;

public class ListPaymentsTests
{
    private static readonly DateTimeOffset start = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Page_that_holds_every_payment_has_no_next_cursor()
    {
        InMemoryPaymentReader reader = new();
        reader.Payments.Add(Summary("pi_1", start));
        reader.Payments.Add(Summary("pi_2", start.AddSeconds(1)));
        ListPayments listPayments = new(reader, new CollectingLogger<ListPayments>());

        ListPaymentsResult result = await listPayments.ExecuteAsync(null, 2, CancellationToken.None);

        Assert.Equal(ListPaymentsOutcome.Listed, result.Outcome);
        Assert.Equal(["pi_2", "pi_1"], result.Page!.Items.Select(payment => payment.PaymentIntentId));
        Assert.Null(result.Page.NextCursor);
        Assert.Null(reader.RequestedAfter);
        Assert.Equal(3, reader.RequestedCount);
    }

    [Fact]
    public async Task Next_cursor_points_after_the_last_payment_of_the_page()
    {
        InMemoryPaymentReader reader = new();
        reader.Payments.Add(Summary("pi_1", start));
        reader.Payments.Add(Summary("pi_2", start.AddSeconds(1)));
        reader.Payments.Add(Summary("pi_3", start.AddSeconds(2)));
        ListPayments listPayments = new(reader, new CollectingLogger<ListPayments>());

        ListPaymentsResult result = await listPayments.ExecuteAsync(null, 2, CancellationToken.None);

        Assert.Equal(["pi_3", "pi_2"], result.Page!.Items.Select(payment => payment.PaymentIntentId));
        Assert.True(PaymentCursor.TryDecode(result.Page.NextCursor!, out PaymentCursor? next));
        Assert.Equal(new PaymentCursor(start.AddSeconds(1), "pi_2"), next);
    }

    [Fact]
    public async Task Following_the_next_cursor_visits_every_payment_once_in_order()
    {
        InMemoryPaymentReader reader = new();
        for (int i = 0; i < 7; i++)
        {
            reader.Payments.Add(Summary($"pi_{i}", start.AddSeconds(i / 3)));
        }

        ListPayments listPayments = new(reader, new CollectingLogger<ListPayments>());
        List<string> visited = [];
        string? cursor = null;
        do
        {
            ListPaymentsResult result = await listPayments.ExecuteAsync(cursor, 2, CancellationToken.None);
            visited.AddRange(result.Page!.Items.Select(payment => payment.PaymentIntentId));
            cursor = result.Page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(["pi_6", "pi_5", "pi_4", "pi_3", "pi_2", "pi_1", "pi_0"], visited);
    }

    [Fact]
    public async Task Invalid_cursor_reads_nothing_and_is_logged_as_a_warning()
    {
        InMemoryPaymentReader reader = new();
        CollectingLogger<ListPayments> logger = new();
        ListPayments listPayments = new(reader, logger);

        ListPaymentsResult result = await listPayments.ExecuteAsync("not a cursor", 2, CancellationToken.None);

        Assert.Equal(ListPaymentsOutcome.InvalidCursor, result.Outcome);
        Assert.Null(result.Page);
        Assert.Null(reader.RequestedCount);
        Assert.Equal([LogLevel.Warning], logger.Levels);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ListPayments.MaxPageSize + 1)]
    public async Task Page_size_out_of_range_throws(int pageSize)
    {
        ListPayments listPayments = new(new InMemoryPaymentReader(), new CollectingLogger<ListPayments>());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => listPayments.ExecuteAsync(null, pageSize, CancellationToken.None));
    }

    private static PaymentSummary Summary(string paymentIntentId, DateTimeOffset updatedAt) =>
        new(paymentIntentId, PaymentStatus.Paid, 1000, 0, "brl", updatedAt);
}
