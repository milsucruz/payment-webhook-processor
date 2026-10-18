using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Payments.Api.Authorization;
using Payments.Application.DeadLetters;

namespace Payments.Api.Controllers;

// Details: docs/code/DeadLettersController.md
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
    public async Task<IReadOnlyList<DeadLetter>> ListAsync(CancellationToken cancellationToken) =>
        await this.listDeadLetters.ExecuteAsync(cancellationToken);

    /// <summary>Sends a dead-lettered message back to the event queue and removes it from the dead-letter.</summary>
    [HttpPost("{sequenceNumber:long}/reprocess")]
    [RequireAdminKey]
    public async Task<IActionResult> ReprocessAsync(long sequenceNumber, CancellationToken cancellationToken)
    {
        ReprocessDeadLetterResult result = await this.reprocessDeadLetter.ExecuteAsync(sequenceNumber, cancellationToken);

        return result switch
        {
            ReprocessDeadLetterResult.Reprocessed => Accepted(),
            ReprocessDeadLetterResult.NotFound => NotFound(),
            _ => throw new UnreachableException(),
        };
    }
}
