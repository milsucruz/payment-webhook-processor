using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Payments.Application.Reading;

// Details: docs/code/PaymentCursor.md
public sealed record PaymentCursor(DateTimeOffset UpdatedAt, string PaymentIntentId)
{
    private const char Separator = '|';

    public static PaymentCursor From(PaymentSummary payment) => new(payment.UpdatedAt, payment.PaymentIntentId);

    public static bool TryDecode(string value, out PaymentCursor? cursor)
    {
        cursor = null;
        if (!Base64Url.IsValid(value))
        {
            return false;
        }

        string text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value));
        int separatorIndex = text.IndexOf(Separator);
        if (separatorIndex <= 0 || separatorIndex == text.Length - 1)
        {
            return false;
        }

        if (!long.TryParse(text.AsSpan(0, separatorIndex), NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        cursor = new PaymentCursor(new DateTimeOffset(ticks, TimeSpan.Zero), text[(separatorIndex + 1)..]);
        return true;
    }

    public string Encode()
    {
        string text = string.Create(CultureInfo.InvariantCulture, $"{this.UpdatedAt.UtcTicks}{Separator}{this.PaymentIntentId}");
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));
    }
}
