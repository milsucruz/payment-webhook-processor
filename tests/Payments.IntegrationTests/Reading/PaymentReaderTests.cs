using Microsoft.Extensions.Logging.Abstractions;
using Payments.Application.Processing;
using Payments.Application.Reading;
using Payments.Domain;
using Payments.Infrastructure.Persistence;

namespace Payments.IntegrationTests.Reading;

[Collection(PaymentsDatabaseCollection.Name)]
public class PaymentReaderTests : IAsyncLifetime
{
    private static readonly DateTimeOffset start = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);

    private readonly PaymentsDatabaseFixture fixture;

    public PaymentReaderTests(PaymentsDatabaseFixture fixture)
    {
        this.fixture = fixture;
    }

    public Task InitializeAsync() => this.fixture.ClearAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Paging_visits_every_payment_once_newest_first_without_offset()
    {
        List<Payment> payments = [];
        for (int i = 0; i < 25; i++)
        {
            payments.Add(new Payment($"pi_{i:D2}", PaymentStatus.Paid, 1000, 0, "brl", $"evt_{i:D2}", start, start.AddSeconds(i / 4)));
        }

        await using (PaymentsDbContext seed = this.fixture.CreateDbContext(null))
        {
            seed.Payments.AddRange(payments);
            await seed.SaveChangesAsync();
        }

        List<string> executedCommands = [];
        await using PaymentsDbContext dbContext = this.fixture.CreateDbContext(executedCommands);
        ListPayments listPayments = new(new PaymentReader(dbContext), NullLogger<ListPayments>.Instance);
        List<PaymentSummary> visited = [];
        int pages = 0;
        string? cursor = null;
        do
        {
            ListPaymentsResult result = await listPayments.ExecuteAsync(cursor, 4, CancellationToken.None);
            visited.AddRange(result.Page!.Items);
            cursor = result.Page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        List<string> expected = payments
            .OrderByDescending(payment => payment.UpdatedAt)
            .ThenByDescending(payment => payment.PaymentIntentId, StringComparer.Ordinal)
            .Select(payment => payment.PaymentIntentId)
            .ToList();
        Assert.Equal(expected, visited.Select(payment => payment.PaymentIntentId));
        Assert.Equal(payments.Select(payment => payment.UpdatedAt).ToHashSet(), visited.Select(payment => payment.UpdatedAt).ToHashSet());
        Assert.Equal(7, pages);
        Assert.Equal(pages, executedCommands.Count);
        Assert.All(executedCommands, command => Assert.DoesNotContain("OFFSET", command, StringComparison.OrdinalIgnoreCase));
        Assert.All(executedCommands, command => Assert.Contains("TOP", command, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Detail_returns_the_history_in_write_order_with_event_type_and_trace()
    {
        await using (PaymentsDbContext seed = this.fixture.CreateDbContext(null))
        {
            seed.Payments.Add(new Payment("pi_1", PaymentStatus.Paid, 1000, 0, "brl", "evt_paid", start, start.AddSeconds(1)));
            seed.Payments.Add(new Payment("pi_other", PaymentStatus.Created, 500, 0, "brl", "evt_other", start, start));
            seed.InboxEvents.Add(Inbox("pi_1", "evt_paid", "payment_intent.succeeded", TransitionOutcome.Applied, "0af7651916cd43dd8448eb211c80319c"));
            seed.InboxEvents.Add(Inbox("pi_other", "evt_other", "payment_intent.created", TransitionOutcome.Applied, null));
            seed.InboxEvents.Add(Inbox("pi_1", "evt_created", "payment_intent.created", TransitionOutcome.IgnoredOutOfOrder, null));
            await seed.SaveChangesAsync();

            seed.PaymentHistory.Add(History("pi_1", "evt_paid", null, PaymentStatus.Paid, true, null, start));
            await seed.SaveChangesAsync();
            seed.PaymentHistory.Add(History("pi_other", "evt_other", null, PaymentStatus.Created, true, null, start));
            await seed.SaveChangesAsync();
            seed.PaymentHistory.Add(History("pi_1", "evt_created", PaymentStatus.Paid, PaymentStatus.Created, false, "Out of order", start.AddSeconds(1)));
            await seed.SaveChangesAsync();
        }

        await using PaymentsDbContext dbContext = this.fixture.CreateDbContext(null);
        PaymentDetail? detail = await new PaymentReader(dbContext).FindAsync("pi_1", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(PaymentStatus.Paid, detail.Status);
        Assert.Equal("evt_paid", detail.LastEventId);
        Assert.Equal(start, detail.CreatedAt);
        Assert.Equal(start.AddSeconds(1), detail.UpdatedAt);
        Assert.Equal(
            [
                new PaymentHistoryItem("evt_paid", "payment_intent.succeeded", null, PaymentStatus.Paid, true, null, start, "0af7651916cd43dd8448eb211c80319c"),
                new PaymentHistoryItem("evt_created", "payment_intent.created", PaymentStatus.Paid, PaymentStatus.Created, false, "Out of order", start.AddSeconds(1), null),
            ],
            detail.History);
    }

    [Fact]
    public async Task Detail_of_an_unknown_payment_is_null()
    {
        await using PaymentsDbContext dbContext = this.fixture.CreateDbContext(null);

        PaymentDetail? detail = await new PaymentReader(dbContext).FindAsync("pi_unknown", CancellationToken.None);

        Assert.Null(detail);
    }

    private static InboxEvent Inbox(string paymentIntentId, string eventId, string eventType, TransitionOutcome outcome, string? traceId) => new()
    {
        EventId = eventId,
        EventType = eventType,
        PaymentIntentId = paymentIntentId,
        StripeCreatedAt = start,
        ReceivedAt = start,
        Outcome = outcome,
        TraceId = traceId,
        Payload = "{}",
    };

    private static PaymentHistoryEntry History(
        string paymentIntentId,
        string eventId,
        PaymentStatus? fromStatus,
        PaymentStatus toStatus,
        bool applied,
        string? reason,
        DateTimeOffset occurredAt) => new()
    {
        PaymentIntentId = paymentIntentId,
        EventId = eventId,
        FromStatus = fromStatus,
        ToStatus = toStatus,
        Applied = applied,
        Reason = reason,
        OccurredAt = occurredAt,
    };
}
