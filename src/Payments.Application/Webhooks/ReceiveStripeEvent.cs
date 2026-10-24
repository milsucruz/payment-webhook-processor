using System.Text.Json;
using Microsoft.Extensions.Logging;
using Payments.Application.Messaging;
using Payments.Application.Telemetry;

namespace Payments.Application.Webhooks;

// Details: docs/code/ReceiveStripeEvent.md
public class ReceiveStripeEvent
{
    private const int MaxEventIdLength = 128;
    private const string UnknownEventType = "unknown";

    private readonly IWebhookSignatureVerifier signatureVerifier;
    private readonly IEventPublisher eventPublisher;
    private readonly PaymentsMetrics metrics;
    private readonly ILogger<ReceiveStripeEvent> logger;

    public ReceiveStripeEvent(
        IWebhookSignatureVerifier signatureVerifier,
        IEventPublisher eventPublisher,
        PaymentsMetrics metrics,
        ILogger<ReceiveStripeEvent> logger)
    {
        this.signatureVerifier = signatureVerifier;
        this.eventPublisher = eventPublisher;
        this.metrics = metrics;
        this.logger = logger;
    }

    public async Task<ReceiveStripeEventResult> ExecuteAsync(string payload, string? signature, CancellationToken cancellationToken)
    {
        SignatureVerificationResult verification = this.signatureVerifier.Verify(payload, signature);
        if (!verification.IsValid)
        {
            this.logger.LogWarning("Rejected Stripe webhook: {Reason}", verification.FailureReason);
            return ReceiveStripeEventResult.InvalidSignature;
        }

        EventEnvelope? envelope = ReadEnvelope(payload);
        if (envelope is null)
        {
            this.logger.LogWarning("Rejected Stripe webhook: the body has no valid event id");
            return ReceiveStripeEventResult.InvalidEventId;
        }

        string eventId = envelope.EventId;
        try
        {
            await this.eventPublisher.PublishAsync(eventId, payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            this.logger.LogWarning("Publishing of event {EventId} was cancelled by the caller", eventId);
            return ReceiveStripeEventResult.PublishFailed;
        }
        catch (Exception exception)
        {
            this.logger.LogError(exception, "Failed to publish Stripe event {EventId}", eventId);
            return ReceiveStripeEventResult.PublishFailed;
        }

        this.logger.LogInformation("Published Stripe event {EventId}", eventId);
        this.metrics.EventReceived(envelope.EventType);
        return ReceiveStripeEventResult.Published;
    }

    private static EventEnvelope? ReadEnvelope(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("id", out JsonElement idElement)
                || idElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string? eventId = idElement.GetString();
            if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > MaxEventIdLength)
            {
                return null;
            }

            string eventType = document.RootElement.TryGetProperty("type", out JsonElement typeElement) && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()!
                : UnknownEventType;
            return new EventEnvelope(eventId, eventType);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record EventEnvelope(string EventId, string EventType);
}
