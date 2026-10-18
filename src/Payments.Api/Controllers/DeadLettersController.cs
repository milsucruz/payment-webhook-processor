using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Payments.Api.Authentication;
using Payments.Application.DeadLetters;

namespace Payments.Api.Controllers;

[ApiController]
[Route("api/dead-letters")]
public class DeadLettersController : ControllerBase
{
    private readonly ListDeadLetters listDeadLetters;
    private readonly ReprocessDeadLetter reprocessDeadLetter;

    public DeadLettersController(ListDeadLetters listDeadLetters, ReprocessDeadLetter reprocessDeadLetter)
    {
        this.listDeadLetters = listDeadLetters;
        this.reprocessDeadLetter = reprocessDeadLetter;
    }

    /// <summary>Lists the messages in the event queue's dead-letter without removing them.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeadLetterMessage>>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DeadLetterMessage> messages = await this.listDeadLetters.ExecuteAsync(cancellationToken);
        return Ok(messages);
    }

    /// <summary>Sends a dead-lettered message back to the event queue and removes it from the dead-letter.</summary>
    [ServiceFilter<AdminKeyFilter>]
    [HttpPost("{sequenceNumber:long}/reprocess")]
    public async Task<IActionResult> ReprocessAsync(long sequenceNumber, CancellationToken cancellationToken)
    {
        ReprocessDeadLetterResult result = await this.reprocessDeadLetter.ExecuteAsync(sequenceNumber, cancellationToken);

        return result switch
        {
            ReprocessDeadLetterResult.Reprocessed => NoContent(),
            ReprocessDeadLetterResult.NotFound => NotFound(),
            ReprocessDeadLetterResult.NotReached => Conflict(),
            _ => throw new UnreachableException(),
        };
    }
}
