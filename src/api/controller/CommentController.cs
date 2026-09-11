using app;
using domain;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace api;

[ApiController]
[Authorize]
[Route("api/v1/[controller]")]
public sealed class CommentController : ControllerBase
{
    private readonly ISender _sender;

    public CommentController(ISender sender)
    {
        _sender = sender;
    }

    [EnableRateLimiting(RateLimiterPolicy.Upload)]
    [HttpPost]
    public async Task<ActionResult<CommentDto>> CreateComment(
        [FromForm] Guid postId,
        [FromForm] string? content,
        [FromForm] List<IFormFile>? images,
        CancellationToken cancellationToken)
    {
        var imageResult = ImageUpload.ToStreams(images);
        if(imageResult.IsFailed)
            return Result.Fail(imageResult.Errors).ToActionResult();

        var result = await _sender.Send(new CreateCommentCommand(postId, content, imageResult.Value), cancellationToken);
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

    [Authorize(Roles = nameof(Role.Admin))]
    [HttpGet]
    public async Task<ActionResult<CursorPageResult<CommentSummaryDto>>> GetAllComment(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetAllCommentQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [Authorize(Roles = nameof(Role.Admin))]
    [HttpDelete("admin/{commentId:guid}")]
    public async Task<ActionResult> AdminDeleteComment(
        Guid commentId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new AdminDeleteCommentCommand(commentId), cancellationToken);
        return result.ToActionResult();
    }
}
