using app;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api;

[ApiController]
[Authorize]
[Route("api/v1/[controller]")]
public sealed class FollowController : ControllerBase
{
    private readonly ISender _sender;

    public FollowController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("{followingId:guid}")]
    public async Task<ActionResult> Follow(
        Guid followingId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new FollowCommand(followingId), cancellationToken);
        return result.ToAcceptedResult();
    }

    [HttpDelete("{followingId:guid}")]
    public async Task<ActionResult> Unfollow(
        Guid followingId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new UnfollowCommand(followingId), cancellationToken);
        return result.ToAcceptedResult();
    }

    [HttpGet("follower/{accountId:guid}")]
    public async Task<ActionResult<CursorPageResult<FollowerDto>>> GetFollowerByAccount(
        Guid accountId,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetFollowerByAccountQuery(accountId, new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("following/{accountId:guid}")]
    public async Task<ActionResult<CursorPageResult<FollowingDto>>> GetFollowingByAccount(
        Guid accountId,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetFollowingByAccountQuery(accountId, new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("me/follower")]
    public async Task<ActionResult<CursorPageResult<FollowerDto>>> GetMyFollower(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetMyFollowerQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("me/following")]
    public async Task<ActionResult<CursorPageResult<FollowingDto>>> GetMyFollowing(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetMyFollowingQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }
}
