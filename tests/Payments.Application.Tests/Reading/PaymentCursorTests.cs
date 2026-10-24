using System.Buffers.Text;
using System.Text;
using Payments.Application.Reading;

namespace Payments.Application.Tests.Reading;

public class PaymentCursorTests
{
    [Fact]
    public void Encoded_cursor_decodes_to_the_same_instant_and_payment()
    {
        PaymentCursor cursor = new(new DateTimeOffset(2026, 10, 24, 12, 30, 15, TimeSpan.Zero).AddTicks(1234567), "pi_1");

        bool decoded = PaymentCursor.TryDecode(cursor.Encode(), out PaymentCursor? result);

        Assert.True(decoded);
        Assert.Equal(cursor, result);
        Assert.Equal(cursor.UpdatedAt.UtcTicks, result!.UpdatedAt.UtcTicks);
    }

    [Fact]
    public void Cursor_is_url_safe()
    {
        PaymentCursor cursor = new(DateTimeOffset.UnixEpoch, "pi_???>>>");

        string encoded = cursor.Encode();

        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);
    }

    [Fact]
    public void Value_that_is_not_base64url_is_rejected()
    {
        bool decoded = PaymentCursor.TryDecode("not base64!", out PaymentCursor? result);

        Assert.False(decoded);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("|pi_1")]
    [InlineData("123|")]
    [InlineData("-1|pi_1")]
    [InlineData("+1|pi_1")]
    [InlineData("abc|pi_1")]
    [InlineData("99999999999999999999|pi_1")]
    [InlineData("3155378976000000000|pi_1")]
    public void Malformed_cursor_is_rejected(string text)
    {
        string value = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));

        bool decoded = PaymentCursor.TryDecode(value, out PaymentCursor? result);

        Assert.False(decoded);
        Assert.Null(result);
    }
}
