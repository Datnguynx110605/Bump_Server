using app;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api;

[ApiController]
[Authorize]
[Route("api/v1/[controller]")]
public sealed class BlockController : ControllerBase
{
    private readonly ISender _sender;

    public BlockController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("{accountId:guid}")]
    public async Task<ActionResult> BlockAccount(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new BlockAccountCommand(accountId), cancellationToken);
        return result.ToAcceptedResult();
    }

    [HttpDelete("{accountId:guid}")]
    public async Task<ActionResult> UnblockAccount(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new UnblockAccountCommand(accountId), cancellationToken);
        return result.ToAcceptedResult();
    }

    [HttpGet("me")]
    public async Task<ActionResult<CursorPageResult<BlockDto>>> GetMyBlockedAccount(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetMyBlockedAccountQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }
}
