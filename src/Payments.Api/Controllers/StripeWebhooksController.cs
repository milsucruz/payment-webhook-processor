using Microsoft.AspNetCore.Mvc;
using Payments.Infrastructure.Webhooks;

namespace Payments.Api.Controllers;

// Details: docs/code/StripeWebhooksController.md
[ApiController]
[Route("webhooks/stripe")]
public class StripeWebhooksController : ControllerBase
{
    // Architecture section 8. Service Bus also counts headers in its 256 KB: see docs/pending.md.
    private const int MaxBodyBytes = 256 * 1024;

    private readonly IWebhookSignatureVerifier signatureVerifier;
    private readonly ILogger<StripeWebhooksController> logger;

    public StripeWebhooksController(
        IWebhookSignatureVerifier signatureVerifier,
        ILogger<StripeWebhooksController> logger)
    {
        this.signatureVerifier = signatureVerifier;
        this.logger = logger;
    }

    /// <summary>Receives a Stripe webhook event and rejects it if the signature is not valid.</summary>
    // No parameters, not even a CancellationToken: any bound parameter lets MVC consume form bodies first.
    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> ReceiveAsync()
    {
        // Raw body, before any deserialization: the signature is checked over it as received.
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var payload = await reader.ReadToEndAsync(HttpContext.RequestAborted);

        var verification = this.signatureVerifier.Verify(payload, Request.Headers["Stripe-Signature"]);
        if (!verification.IsValid)
        {
            this.logger.LogWarning("Rejected Stripe webhook: {Reason}", verification.FailureReason);
            return BadRequest();
        }

        return Ok();
    }
}
