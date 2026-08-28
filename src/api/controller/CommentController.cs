using app;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class CommentController : ControllerBase
{
    private readonly ISender _sender;

    public CommentController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    public async Task<ActionResult<CommentDto>> CreateComment(
        [FromForm] Guid postId,
        [FromForm] string? content,
        [FromForm] List<IFormFile>? images,
        CancellationToken cancellationToken)
    {
        var imageStreams = images?
            .Select(x => (x.OpenReadStream(), x.FileName))
            .ToList();

        var result = await _sender.Send(new CreateCommentCommand(postId, content, imageStreams), cancellationToken);
        return result.ToActionResult();
    }

    [HttpPut("{commentId:guid}")]
    public async Task<ActionResult> ChangeContent(
        Guid commentId,
        ChangeContentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ChangeContentCommand(commentId, request.Content), cancellationToken);
        return result.ToActionResult();
    }

    [HttpDelete("{commentId:guid}")]
    public async Task<ActionResult> DeleteComment(
        Guid commentId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new DeleteCommentCommand(commentId), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("{commentId:guid}")]
    public async Task<ActionResult<CommentSummaryDto>> GetSpecificComment(
        Guid commentId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetSpecificCommentQuery(commentId), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("post/{postId:guid}")]
    public async Task<ActionResult<CursorPageResult<CommentSummaryDto>>> GetCommentByPost(
        Guid postId,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetCommentByPostQuery(postId, new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("mine")]
    public async Task<ActionResult<CursorPageResult<CommentSummaryDto>>> GetMyCommentHistory(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetMyCommentHistoryQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }
}
