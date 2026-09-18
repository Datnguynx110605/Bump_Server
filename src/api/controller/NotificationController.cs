using app;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api;

[ApiController]
[Authorize]
[Route("api/v1/[controller]")]
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

    [HttpPost("device")]
    public async Task<ActionResult> RegisterDevice(
        [FromBody] RegisterDeviceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new RegisterDeviceCommand(request.Token, request.IsSandbox),
            cancellationToken);

        return result.ToActionResult();
    }

    // The token travels in the path rather than a body because a DELETE with a body is
    // awkward for most HTTP clients, and the token is a plain hex string.
    [HttpDelete("device/{token}")]
    public async Task<ActionResult> UnregisterDevice(
        string token,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new UnregisterDeviceCommand(token), cancellationToken);
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
