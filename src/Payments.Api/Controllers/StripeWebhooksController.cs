using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Payments.Application.Messaging;
using Payments.Infrastructure.Webhooks;

namespace Payments.Api.Controllers;

// Details: docs/code/StripeWebhooksController.md
[ApiController]
[Route("webhooks/stripe")]
public class StripeWebhooksController : ControllerBase
{
    // Architecture section 8. Service Bus also counts headers in its 256 KB: see the class doc.
    private const int MaxBodyBytes = 256 * 1024;

    private const int MaxEventIdLength = 128;

    private readonly IWebhookSignatureVerifier signatureVerifier;
    private readonly IEventPublisher eventPublisher;
    private readonly ILogger<StripeWebhooksController> logger;

    public StripeWebhooksController(
        IWebhookSignatureVerifier signatureVerifier,
        IEventPublisher eventPublisher,
        ILogger<StripeWebhooksController> logger)
    {
        this.signatureVerifier = signatureVerifier;
        this.eventPublisher = eventPublisher;
        this.logger = logger;
    }

    /// <summary>Receives a Stripe webhook event, checks its signature and publishes it to the event queue.</summary>
    // No parameters, not even a CancellationToken: any bound parameter lets MVC consume form bodies first.
    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> ReceiveAsync()
    {
        // Raw body, before any deserialization: the signature is checked over it as received.
        using StreamReader reader = new(Request.Body, leaveOpen: true);
        string payload = await reader.ReadToEndAsync(HttpContext.RequestAborted);

        SignatureVerificationResult verification = this.signatureVerifier.Verify(payload, Request.Headers["Stripe-Signature"]);
        if (!verification.IsValid)
        {
            this.logger.LogWarning("Rejected Stripe webhook: {Reason}", verification.FailureReason);
            return BadRequest();
        }

        string? eventId = ReadEventId(payload);
        if (eventId is null)
        {
            this.logger.LogWarning("Rejected Stripe webhook: the body has no valid event id");
            return BadRequest();
        }

        try
        {
            await this.eventPublisher.PublishAsync(eventId, payload, HttpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            this.logger.LogWarning("Stripe closed the connection before event {EventId} was published", eventId);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
        catch (Exception exception)
        {
            this.logger.LogError(exception, "Failed to publish Stripe event {EventId}", eventId);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok();
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
