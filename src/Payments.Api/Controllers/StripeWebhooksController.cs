using Microsoft.AspNetCore.Mvc;

namespace Payments.Api.Controllers;

// Details: docs/code/StripeWebhooksController.md
[ApiController]
[Route("webhooks/stripe")]
public class StripeWebhooksController : ControllerBase
{
    // Architecture section 8. Service Bus also counts headers in its 256 KB: see docs/pending.md.
    private const int MaxBodyBytes = 256 * 1024;

    /// <summary>Receives a Stripe webhook event.</summary>
    // No parameters, not even a CancellationToken: any bound parameter lets MVC consume form bodies first.
    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive()
    {
        // Raw body, before any deserialization: the signature is checked over it as received.
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        await reader.ReadToEndAsync(HttpContext.RequestAborted);

        return Ok();
    }
}
