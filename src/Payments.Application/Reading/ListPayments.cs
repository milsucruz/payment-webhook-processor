using Microsoft.Extensions.Logging;

namespace Payments.Application.Reading;

// Details: docs/code/ListPayments.md
public sealed class ListPayments
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    private readonly IPaymentReader paymentReader;
    private readonly ILogger<ListPayments> logger;

    public ListPayments(IPaymentReader paymentReader, ILogger<ListPayments> logger)
    {
        this.paymentReader = paymentReader;
        this.logger = logger;
    }

    public async Task<ListPaymentsResult> ExecuteAsync(string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, MaxPageSize);

        PaymentCursor? after = null;
        if (cursor is not null && !PaymentCursor.TryDecode(cursor, out after))
        {
            this.logger.LogWarning("Payment list cursor {Cursor} is not valid", cursor);
            return new ListPaymentsResult(ListPaymentsOutcome.InvalidCursor);
        }

        IReadOnlyList<PaymentSummary> rows = await this.paymentReader.ListAsync(after, pageSize + 1, cancellationToken);
        if (rows.Count <= pageSize)
        {
            return new ListPaymentsResult(ListPaymentsOutcome.Listed, new PaymentPage(rows, null));
        }

        List<PaymentSummary> items = rows.Take(pageSize).ToList();
        string nextCursor = PaymentCursor.From(items[^1]).Encode();
        return new ListPaymentsResult(ListPaymentsOutcome.Listed, new PaymentPage(items, nextCursor));
    }
}
