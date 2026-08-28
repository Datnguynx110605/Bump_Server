using app;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class FollowController : ControllerBase
{
    private readonly ISender _sender;

    public FollowController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("{followingId:guid}")]
    public async Task<ActionResult> Follow(Guid followingId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new FollowCommand(followingId), cancellationToken);
        return result.ToAcceptedResult();
    }

    [HttpDelete("{followingId:guid}")]
    public async Task<ActionResult> Unfollow(Guid followingId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new UnfollowCommand(followingId), cancellationToken);
        return result.ToAcceptedResult();
    }
}
