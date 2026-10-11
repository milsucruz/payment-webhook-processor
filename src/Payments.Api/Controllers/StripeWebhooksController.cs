using Microsoft.AspNetCore.Mvc;

namespace Payments.Api.Controllers;

[ApiController]
[Route("webhooks/stripe")]
public class StripeWebhooksController : ControllerBase
{
    // The webhook body limit from architecture section 8. The Service Bus Basic cap is also
    // 256 KB but counts headers too, so a body near this limit may not fit in a message:
    // see docs/pending.md.
    private const int MaxBodyBytes = 256 * 1024;

    // The action takes no parameters on purpose. Any bound parameter, even a CancellationToken,
    // makes MVC build value providers, and the form provider reads a form-encoded body before
    // the action runs, leaving nothing for the raw read below.
    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive()
    {
        // Read the raw body as UTF-8 text, before any deserialization: the Stripe signature
        // is checked over this text, exactly as received. Reading it also enforces the size
        // limit for chunked requests, which have no Content-Length to reject upfront.
        // leaveOpen: the request stream belongs to the server, not to this reader.
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        // For now the payload is discarded: task 1.2 validates the signature over it
        // and task 1.4 publishes it.
        await reader.ReadToEndAsync(HttpContext.RequestAborted);

        return Ok();
    }
}
