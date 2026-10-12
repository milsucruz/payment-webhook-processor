using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Payments.Application.Webhooks;

namespace Payments.Api.Controllers;

// Details: docs/code/StripeWebhooksController.md
[ApiController]
[Route("webhooks/stripe")]
public class StripeWebhooksController : ControllerBase
{
    // Architecture section 8. Service Bus also counts headers in its 256 KB: see the class doc.
    private const int MaxBodyBytes = 256 * 1024;

    private readonly ReceiveStripeEvent receiveStripeEvent;

    public StripeWebhooksController(ReceiveStripeEvent receiveStripeEvent)
    {
        this.receiveStripeEvent = receiveStripeEvent;
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

        ReceiveStripeEventResult result = await this.receiveStripeEvent.ExecuteAsync(
            payload,
            Request.Headers["Stripe-Signature"],
            HttpContext.RequestAborted);

        return result switch
        {
            ReceiveStripeEventResult.Published => Ok(),
            ReceiveStripeEventResult.InvalidSignature or ReceiveStripeEventResult.InvalidEventId => BadRequest(),
            ReceiveStripeEventResult.PublishFailed => StatusCode(StatusCodes.Status500InternalServerError),
            _ => throw new UnreachableException(),
        };
    }
}
