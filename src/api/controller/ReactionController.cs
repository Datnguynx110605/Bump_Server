using app;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api;

[ApiController]
[Authorize]
[Route("api/v1/[controller]")]
public sealed class ReactionController : ControllerBase
{
    private readonly ISender _sender;

    public ReactionController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("post/{postId:guid}")]
    public async Task<ActionResult> ReactPost(
        Guid postId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ReactPostCommand(postId), cancellationToken);
        return result.ToAcceptedResult();
    }

    [HttpDelete("post/{postId:guid}")]
    public async Task<ActionResult> UnreactPost(
        Guid postId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new UnreactPostCommand(postId), cancellationToken);
        return result.ToAcceptedResult();
    }

    [HttpPost("comment/{commentId:guid}")]
    public async Task<ActionResult> ReactComment(
        Guid commentId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ReactCommentCommand(commentId), cancellationToken);
        return result.ToAcceptedResult();
    }

    [HttpDelete("comment/{commentId:guid}")]
    public async Task<ActionResult> UnreactComment(
        Guid commentId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new UnreactCommentCommand(commentId), cancellationToken);
        return result.ToAcceptedResult();
    }

    [HttpGet("post/{postId:guid}")]
    public async Task<ActionResult<CursorPageResult<ReactionDto>>> GetReactionByPost(
        Guid postId,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetReactionByPostQuery(postId, new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("comment/{commentId:guid}")]
    public async Task<ActionResult<CursorPageResult<ReactionDto>>> GetReactionByComment(
        Guid commentId,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetReactionByCommentQuery(commentId, new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }
}
