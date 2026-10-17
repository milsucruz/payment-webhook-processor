using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Payments.Infrastructure.Persistence;

namespace Payments.Infrastructure.Tests.Persistence;

public class SqlTransientErrorDetectorTests
{
    private readonly SqlTransientErrorDetector detector = new();

    [Fact]
    public async Task Connection_that_cannot_reach_the_server_is_transient()
    {
        await using SqlConnection connection = new("Server=127.0.0.1,1;Connect Timeout=1;Encrypt=False");

        SqlException exception = await Assert.ThrowsAsync<SqlException>(() => connection.OpenAsync());

        Assert.True(this.detector.IsTransient(exception));
        Assert.True(this.detector.IsTransient(new DbUpdateException("save failed", exception)));
    }

    [Fact]
    public void Timeout_is_transient_even_when_wrapped()
    {
        Assert.True(this.detector.IsTransient(new TimeoutException()));
        Assert.True(this.detector.IsTransient(new DbUpdateException("save failed", new TimeoutException())));
    }

    [Fact]
    public void Other_exceptions_are_not_transient()
    {
        Assert.False(this.detector.IsTransient(new InvalidOperationException()));
        Assert.False(this.detector.IsTransient(new OperationCanceledException()));
        Assert.False(this.detector.IsTransient(new DbUpdateException("save failed", new InvalidOperationException())));
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(40)]
    [InlineData(53)]
    [InlineData(121)]
    [InlineData(258)]
    [InlineData(1205)]
    [InlineData(1222)]
    [InlineData(10061)]
    [InlineData(10065)]
    [InlineData(11001)]
    [InlineData(40613)]
    [InlineData(40501)]
    public void Transient_error_numbers_are_recognized(int number)
    {
        Assert.True(SqlTransientErrorDetector.IsTransientErrorNumber(number));
    }

    [Theory]
    [InlineData(2627)]
    [InlineData(547)]
    [InlineData(207)]
    public void Errors_that_retrying_cannot_fix_are_not_transient(int number)
    {
        Assert.False(SqlTransientErrorDetector.IsTransientErrorNumber(number));
    }
}
