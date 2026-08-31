using app;
using domain;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api;

[ApiController]
[Authorize(Roles = nameof(Role.Admin))]
[Route("api/v1/[controller]")]
public sealed class DashboardController : ControllerBase
{
    private readonly ISender _sender;

    public DashboardController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> GetStats(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetDashboardStatsQuery(), cancellationToken);
        return result.ToActionResult();
    }
}
