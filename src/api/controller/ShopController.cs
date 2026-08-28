using app;
using domain;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace api;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class ShopController : ControllerBase
{
    private readonly ISender _sender;

    public ShopController(ISender sender)
    {
        _sender = sender;
    }

    [Authorize(Roles = nameof(Role.Admin))]
    [HttpDelete("{shopId:guid}")]
    public async Task<ActionResult> DeleteShop(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new DeleteShopCommand(shopId), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("{shopId:guid}")]
    public async Task<ActionResult<ShopDto>> GetSpecificShop(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetSpecificShopQuery(shopId), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet]
    public async Task<ActionResult<CursorPageResult<ShopSummaryDto>>> GetAllShop(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetAllShopQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [EnableRateLimiting(RateLimiterPolicy.Search)]
    [HttpGet("search")]
    public async Task<ActionResult<CursorPageResult<ShopSummaryDto>>> GetShopByKeyword(
        [FromQuery] string keyword,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetShopByKeywordQuery(keyword, new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("by-name")]
    public async Task<ActionResult<CursorPageResult<ShopSummaryDto>>> GetShopByName(
        [FromQuery] string name,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetShopByNameQuery(name, new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("by-address")]
    public async Task<ActionResult<CursorPageResult<ShopSummaryDto>>> GetShopByAddress(
        [FromQuery] string address,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetShopByAddressQuery(address, new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("most-rated")]
    public async Task<ActionResult<CursorPageResult<ShopSummaryDto>>> GetMostRatedShop(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetMostRatedShopQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }
}
