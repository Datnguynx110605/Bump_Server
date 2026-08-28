using app;
using domain;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class PostController : ControllerBase
{
    private readonly ISender _sender;

    public PostController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    public async Task<ActionResult<PostDto>> CreatePost(
        [FromForm] string shopName,
        [FromForm] string shopAddress,
        [FromForm] string? caption,
        [FromForm] int rate,
        [FromForm] List<IFormFile>? images,
        CancellationToken cancellationToken)
    {
        var rateResult = Amount.Create(rate);
        if(!rateResult.IsSuccess)
            return Result.Fail<PostDto>(rateResult.Error!).ToActionResult();

        var imageStreams = images?
            .Select(x => (x.OpenReadStream(), x.FileName))
            .ToList();

        var result = await _sender.Send(new CreatePostCommand(shopName, shopAddress, caption, rateResult.Value!, imageStreams), cancellationToken);
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
    public async Task<ActionResult<PostSummaryDto>> GetSpecificPost(
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

    [HttpGet("search")]
    public async Task<ActionResult<CursorPageResult<PostSummaryDto>>> GetPostByKeyword(
        [FromQuery] string keyword,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetPostByKeywordQuery(keyword, new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("latest")]
    public async Task<ActionResult<CursorPageResult<PostSummaryDto>>> GetLatestPost(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetLatestPostQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("most-reacted")]
    public async Task<ActionResult<CursorPageResult<PostSummaryDto>>> GetMostReactedPost(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetMostReactedPostQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }
}
