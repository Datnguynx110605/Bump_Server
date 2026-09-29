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
public sealed class PostController : ControllerBase
{
    private readonly ISender _sender;

    public PostController(ISender sender)
    {
        _sender = sender;
    }

    [EnableRateLimiting(RateLimiterPolicy.Upload)]
    [HttpPost]
    public async Task<ActionResult<PostDto>> CreatePost(
        [FromForm] string placeId,
        [FromForm] string? caption,
        [FromForm] int rate,
        [FromForm] List<IFormFile>? images,
        CancellationToken cancellationToken)
    {
        var rateResult = Amount.Create(rate);
        if(!rateResult.IsSuccess)
            return Result.Fail<PostDto>(rateResult.Error!).ToActionResult();

        var imageResult = ImageUpload.ToStreams(images);
        if(imageResult.IsFailed)
            return Result.Fail(imageResult.Errors).ToActionResult();

        var result = await _sender.Send(new CreatePostCommand(placeId, caption, rateResult.Value!, imageResult.Value), cancellationToken);
        return result.ToActionResult();
    }

    [HttpPut("{postId:guid}/caption")]
    public async Task<ActionResult> ChangeCaption(
        Guid postId,
        ChangeCaptionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ChangeCaptionCommand(postId, request.Caption), cancellationToken);
        return result.ToActionResult();
    }

    [HttpPut("{postId:guid}/rate")]
    public async Task<ActionResult> ChangeRate(
        Guid postId,
        ChangeRateRequest request,
        CancellationToken cancellationToken)
    {
        var rateResult = Amount.Create(request.Rate);
        if(!rateResult.IsSuccess)
            return Result.Fail(rateResult.Error!).ToActionResult();

        var result = await _sender.Send(new ChangeRateCommand(postId, rateResult.Value!), cancellationToken);
        return result.ToActionResult();
    }

    [HttpDelete("{postId:guid}")]
    public async Task<ActionResult> DeletePost(
        Guid postId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new DeletePostCommand(postId), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("{postId:guid}")]
    public async Task<ActionResult<PostFeedItemDto>> GetSpecificPost(
        Guid postId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetSpecificPostQuery(postId), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("mine")]
    public async Task<ActionResult<CursorPageResult<PostSummaryDto>>> GetMyPost(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetMyPostQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("account/{accountId:guid}")]
    public async Task<ActionResult<CursorPageResult<PostSummaryDto>>> GetPostByAccount(
        Guid accountId,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetPostByAccountQuery(accountId, new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("shop/{shopId:guid}")]
    public async Task<ActionResult<CursorPageResult<PostSummaryDto>>> GetPostByShop(
        Guid shopId,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetPostByShopQuery(shopId, new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    // Not a keyword search, but the same order of cost: up to four Mongo queries plus a
    // $sortByCount over every post the viewer has ever reacted to when the affinity cache
    // is cold.
    [EnableRateLimiting(RateLimiterPolicy.Search)]
    [HttpGet("related")]
    public async Task<ActionResult<CursorPageResult<PostFeedItemDto>>> GetRelatedPost(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetRelatedPostQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [EnableRateLimiting(RateLimiterPolicy.Search)]
    [HttpGet("search")]
    public async Task<ActionResult<CursorPageResult<PostFeedItemDto>>> GetPostByKeyword(
        [FromQuery] string keyword,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetPostByKeywordQuery(keyword, new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [Authorize(Roles = nameof(Role.Admin))]
    [HttpGet]
    public async Task<ActionResult<CursorPageResult<PostSummaryDto>>> GetAllPost(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetAllPostQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [Authorize(Roles = nameof(Role.Admin))]
    [HttpDelete("admin/{postId:guid}")]
    public async Task<ActionResult> AdminDeletePost(
        Guid postId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new AdminDeletePostCommand(postId), cancellationToken);
        return result.ToActionResult();
    }
}
