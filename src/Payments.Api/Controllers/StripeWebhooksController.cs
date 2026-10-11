using Microsoft.AspNetCore.Mvc;

namespace Payments.Api.Controllers;

[ApiController]
[Route("webhooks/stripe")]
public class StripeWebhooksController : ControllerBase
{
    // 256 KB, the Service Bus Basic message cap (architecture section 8).
    private const int MaxBodyBytes = 256 * 1024;

    // Stripe always sends JSON. Restricting the content type also stops MVC from parsing a
    // form body before this action runs, which would consume the raw body we must sign-check.
    [HttpPost]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        // Read the raw body as text, before any deserialization: the Stripe signature
        // is computed over these exact bytes. Reading it also enforces the size limit
        // for chunked requests, which have no Content-Length to reject upfront.
        using var reader = new StreamReader(Request.Body);
        // For now the payload is discarded: task 1.2 validates the signature over it
        // and task 1.4 publishes it.
        await reader.ReadToEndAsync(cancellationToken);

        return Ok();
    }
}
