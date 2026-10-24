using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Payments.Application.Reading;

namespace Payments.Api.Controllers;

// Details: docs/code/PaymentsController.md
[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly ListPayments listPayments;
    private readonly GetPayment getPayment;

    public PaymentsController(ListPayments listPayments, GetPayment getPayment)
    {
        this.listPayments = listPayments;
        this.getPayment = getPayment;
    }

    /// <summary>Lists payments one page at a time, most recently updated first.</summary>
    [HttpGet]
    public async Task<IActionResult> ListAsync(
        string? cursor,
        [Range(1, ListPayments.MaxPageSize)] int limit = ListPayments.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        ListPaymentsResult result = await this.listPayments.ExecuteAsync(cursor, limit, cancellationToken);

        switch (result.Outcome)
        {
            case ListPaymentsOutcome.Listed:
                return Ok(result.Page);
            case ListPaymentsOutcome.InvalidCursor:
                ModelState.AddModelError(nameof(cursor), "The cursor is not valid.");
                return ValidationProblem();
            default:
                throw new UnreachableException();
        }
    }

    /// <summary>Returns a payment with the history of every event processed for it.</summary>
    [HttpGet("{paymentIntentId}")]
    public async Task<IActionResult> GetAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        GetPaymentResult result = await this.getPayment.ExecuteAsync(paymentIntentId, cancellationToken);

        return result.Outcome switch
        {
            GetPaymentOutcome.Found => Ok(result.Payment),
            GetPaymentOutcome.NotFound => NotFound(),
            _ => throw new UnreachableException(),
        };
    }
}
