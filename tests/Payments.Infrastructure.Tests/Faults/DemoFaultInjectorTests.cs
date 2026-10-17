using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Payments.Application.Events;
using Payments.Domain;
using Payments.Infrastructure.Faults;
using Payments.Infrastructure.Persistence;

namespace Payments.Infrastructure.Tests.Faults;

public class DemoFaultInjectorTests
{
    private readonly FakeTimeProvider timeProvider = new();
    private readonly SqlTransientErrorDetector transientErrorDetector = new();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Transient_fails_the_first_two_attempts_with_an_error_classified_as_transient(int attempt)
    {
        DemoFaultInjector injector = this.CreateInjector();

        Exception exception = await Assert.ThrowsAnyAsync<Exception>(() => injector.InjectAsync(EventWith("transient"), attempt, CancellationToken.None));

        Assert.True(this.transientErrorDetector.IsTransient(exception));
        Assert.Contains("evt_1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transient_passes_on_the_third_attempt()
    {
        DemoFaultInjector injector = this.CreateInjector();

        await injector.InjectAsync(EventWith("transient"), 3, CancellationToken.None);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Poison_fails_every_attempt_with_an_error_not_classified_as_transient(int attempt)
    {
        DemoFaultInjector injector = this.CreateInjector();

        Exception exception = await Assert.ThrowsAnyAsync<Exception>(() => injector.InjectAsync(EventWith("poison"), attempt, CancellationToken.None));

        Assert.False(this.transientErrorDetector.IsTransient(exception));
    }

    [Fact]
    public async Task Slow_waits_ten_seconds_and_then_lets_the_attempt_continue()
    {
        DemoFaultInjector injector = this.CreateInjector();

        Task injection = injector.InjectAsync(EventWith("slow"), 1, CancellationToken.None);
        this.timeProvider.Advance(TimeSpan.FromSeconds(9));
        Assert.False(injection.IsCompleted);
        this.timeProvider.Advance(TimeSpan.FromSeconds(1));

        await injection;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData("Poison")]
    public async Task No_or_unknown_demo_fault_does_nothing(string? demoFault)
    {
        DemoFaultInjector injector = this.CreateInjector();

        Task injection = injector.InjectAsync(EventWith(demoFault), 1, CancellationToken.None);

        Assert.True(injection.IsCompletedSuccessfully);
        await injection;
    }

    private static PaymentEvent EventWith(string? demoFault)
    {
        return new PaymentEvent("evt_1", "payment_intent.succeeded", DateTimeOffset.UnixEpoch, "pi_1", PaymentStatus.Paid, 2000, 0, "usd", demoFault);
    }

    private DemoFaultInjector CreateInjector()
    {
        return new DemoFaultInjector(this.timeProvider, NullLogger<DemoFaultInjector>.Instance);
    }
}
