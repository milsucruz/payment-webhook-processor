using Microsoft.Extensions.Logging;
using Payments.Application.Messaging;
using Payments.Application.Webhooks;

namespace Payments.Application.Tests.Webhooks;

public class ReceiveStripeEventTests
{
    private const string EventJson = """{"id":"evt_1PtTest","object":"event","type":"payment_intent.succeeded"}""";
    private const string Signature = "t=1,v1=test";

    private readonly CollectingLogger<ReceiveStripeEvent> logger = new();

    [Fact]
    public async Task Valid_signature_and_event_id_publishes_the_original_body_and_returns_published()
    {
        RecordingEventPublisher publisher = new();

        ReceiveStripeEventResult result = await this.Create(SignatureVerificationResult.Valid(), publisher)
            .ExecuteAsync(EventJson, Signature, CancellationToken.None);

        Assert.Equal(ReceiveStripeEventResult.Published, result);
        PublishedEvent published = Assert.Single(publisher.Published);
        Assert.Equal("evt_1PtTest", published.EventId);
        Assert.Equal(EventJson, published.Json);
        LogEntry entry = Assert.Single(this.logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("evt_1PtTest", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_signature_returns_invalid_signature_and_publishes_nothing()
    {
        RecordingEventPublisher publisher = new();

        ReceiveStripeEventResult result = await this.Create(SignatureVerificationResult.Invalid("bad signature"), publisher)
            .ExecuteAsync(EventJson, Signature, CancellationToken.None);

        Assert.Equal(ReceiveStripeEventResult.InvalidSignature, result);
        Assert.Empty(publisher.Published);
        LogEntry entry = Assert.Single(this.logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("bad signature", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_signature_is_reported_before_the_body_is_parsed()
    {
        ReceiveStripeEventResult result = await this.Create(SignatureVerificationResult.Invalid("bad signature"), new RecordingEventPublisher())
            .ExecuteAsync("not json", Signature, CancellationToken.None);

        Assert.Equal(ReceiveStripeEventResult.InvalidSignature, result);
    }

    [Theory]
    [InlineData("""{"object":"event"}""")]
    [InlineData("""{"id":""}""")]
    [InlineData("""{"id":"   "}""")]
    [InlineData("""{"id":123}""")]
    [InlineData("""["evt_1PtTest"]""")]
    [InlineData("not json")]
    public async Task Body_without_a_valid_event_id_returns_invalid_event_id_and_publishes_nothing(string body)
    {
        RecordingEventPublisher publisher = new();

        ReceiveStripeEventResult result = await this.Create(SignatureVerificationResult.Valid(), publisher)
            .ExecuteAsync(body, Signature, CancellationToken.None);

        Assert.Equal(ReceiveStripeEventResult.InvalidEventId, result);
        Assert.Empty(publisher.Published);
        Assert.Equal([LogLevel.Warning], this.logger.Levels);
    }

    [Fact]
    public async Task Event_id_longer_than_128_characters_returns_invalid_event_id()
    {
        RecordingEventPublisher publisher = new();
        string body = $$"""{"id":"evt_{{new string('a', 125)}}"}""";

        ReceiveStripeEventResult result = await this.Create(SignatureVerificationResult.Valid(), publisher)
            .ExecuteAsync(body, Signature, CancellationToken.None);

        Assert.Equal(ReceiveStripeEventResult.InvalidEventId, result);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Publisher_failure_returns_publish_failed_and_logs_an_error()
    {
        ThrowingEventPublisher publisher = new(new InvalidOperationException("The queue is unavailable."));

        ReceiveStripeEventResult result = await this.Create(SignatureVerificationResult.Valid(), publisher)
            .ExecuteAsync(EventJson, Signature, CancellationToken.None);

        Assert.Equal(ReceiveStripeEventResult.PublishFailed, result);
        this.AssertSingleLogWithEventId(LogLevel.Error);
    }

    [Fact]
    public async Task Cancelled_publish_returns_publish_failed_and_logs_a_warning()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        ReceiveStripeEventResult result = await this.Create(SignatureVerificationResult.Valid(), new CancellationObservingEventPublisher())
            .ExecuteAsync(EventJson, Signature, cancellation.Token);

        Assert.Equal(ReceiveStripeEventResult.PublishFailed, result);
        this.AssertSingleLogWithEventId(LogLevel.Warning);
    }

    [Fact]
    public async Task Cancellation_not_requested_by_the_caller_is_logged_as_an_error()
    {
        ThrowingEventPublisher publisher = new(new OperationCanceledException("The publish timed out."));

        ReceiveStripeEventResult result = await this.Create(SignatureVerificationResult.Valid(), publisher)
            .ExecuteAsync(EventJson, Signature, CancellationToken.None);

        Assert.Equal(ReceiveStripeEventResult.PublishFailed, result);
        this.AssertSingleLogWithEventId(LogLevel.Error);
    }

    private ReceiveStripeEvent Create(SignatureVerificationResult verification, IEventPublisher publisher) =>
        new(new FakeSignatureVerifier(verification), publisher, this.logger);

    private void AssertSingleLogWithEventId(LogLevel level)
    {
        LogEntry entry = Assert.Single(this.logger.Entries);
        Assert.Equal(level, entry.Level);
        Assert.Contains("evt_1PtTest", entry.Message, StringComparison.Ordinal);
    }
}
