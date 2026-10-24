using System.Text.Json;
using Microsoft.Extensions.Logging;
using Payments.Application.Messaging;

namespace Payments.Application.Webhooks;

// Details: docs/code/ReceiveStripeEvent.md
public class ReceiveStripeEvent
{
    private const int MaxEventIdLength = 128;

    private readonly IWebhookSignatureVerifier signatureVerifier;
    private readonly IEventPublisher eventPublisher;
    private readonly ILogger<ReceiveStripeEvent> logger;

    public ReceiveStripeEvent(
        IWebhookSignatureVerifier signatureVerifier,
        IEventPublisher eventPublisher,
        ILogger<ReceiveStripeEvent> logger)
    {
        this.signatureVerifier = signatureVerifier;
        this.eventPublisher = eventPublisher;
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

        string? eventId = ReadEventId(payload);
        if (eventId is null)
        {
            this.logger.LogWarning("Rejected Stripe webhook: the body has no valid event id");
            return ReceiveStripeEventResult.InvalidEventId;
        }

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
        return ReceiveStripeEventResult.Published;
    }

    private static string? ReadEventId(string payload)
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
            return string.IsNullOrWhiteSpace(eventId) || eventId.Length > MaxEventIdLength ? null : eventId;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
