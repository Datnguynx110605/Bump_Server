using app;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class SaveController : ControllerBase
{
    private readonly ISender _sender;

    public SaveController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("post/{postId:guid}")]
    public async Task<ActionResult> SavePost(
        Guid postId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new SavePostCommand(postId), cancellationToken);
        return result.ToAcceptedResult();
    }

    [HttpDelete("post/{postId:guid}")]
    public async Task<ActionResult> UnsavePost(
        Guid postId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new UnsavePostCommand(postId), cancellationToken);
        return result.ToAcceptedResult();
    }

    [HttpPost("shop/{shopId:guid}")]
    public async Task<ActionResult> SaveShop(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new SaveShopCommand(shopId), cancellationToken);
        return result.ToAcceptedResult();
    }

    [HttpDelete("shop/{shopId:guid}")]
    public async Task<ActionResult> UnsaveShop(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new UnsaveShopCommand(shopId), cancellationToken);
        return result.ToAcceptedResult();
    }

    [HttpGet("me/post")]
    public async Task<ActionResult<CursorPageResult<PostSummaryDto>>> GetMySavedPosts(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetMySavedPostsQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("me/shop")]
    public async Task<ActionResult<CursorPageResult<ShopSummaryDto>>> GetMySavedShops(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetMySavedShopsQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }
}
