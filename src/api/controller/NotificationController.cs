using app;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class NotificationController : ControllerBase
{
    private readonly ISender _sender;

    public NotificationController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("{notificationId:guid}")]
    public async Task<ActionResult<NotificationSummaryDto>> GetSpecificNotification(
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetSpecificNotificationQuery(notificationId), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("mine")]
    public async Task<ActionResult<CursorPageResult<NotificationSummaryDto>>> GetMyNotification(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetMyNotificationQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }
}
