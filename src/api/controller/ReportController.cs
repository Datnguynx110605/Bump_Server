using app;
using domain;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api;

[ApiController]
[Authorize]
[Route("api/v1/[controller]")]
public sealed class ReportController : ControllerBase
{
    private readonly ISender _sender;

    public ReportController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    public async Task<ActionResult<ReportDto>> CreateReport(
        CreateReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new CreateReportCommand(request.PostId, request.CommentId, request.AccountId, request.Reason), cancellationToken);
        return result.ToActionResult();
    }

    [Authorize(Roles = nameof(Role.Admin))]
    [HttpGet]
    public async Task<ActionResult<CursorPageResult<ReportDto>>> GetAllReport(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetAllReportQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [Authorize(Roles = nameof(Role.Admin))]
    [HttpGet("{reportId:guid}")]
    public async Task<ActionResult<ReportDto>> GetSpecificReport(
        Guid reportId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetSpecificReportQuery(reportId), cancellationToken);
        return result.ToActionResult();
    }

    [Authorize(Roles = nameof(Role.Admin))]
    [HttpGet("target")]
    public async Task<ActionResult<CursorPageResult<ReportDto>>> GetReportByTarget(
        [FromQuery] ReportTarget target,
        [FromQuery] Guid targetId,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetReportByTargetQuery(target, targetId, new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }
}
