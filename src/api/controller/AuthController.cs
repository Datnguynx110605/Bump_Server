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
public sealed class AuthController : ControllerBase
{
    private readonly ISender _sender;

    public AuthController(ISender sender)
    {
        _sender = sender;
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimiterPolicy.Auth)]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResult>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(request.Email);
        if(!emailResult.IsSuccess)
            return Result.Fail<AuthResult>(emailResult.Error!).ToActionResult();

        var result = await _sender.Send(new LoginCommand(emailResult.Value!, request.Password), cancellationToken);
        return result.ToActionResult();
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimiterPolicy.Auth)]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResult>> RequestNewAccessToken(
        RequestNewAccessTokenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new RequestNewAccessTokenCommand(request.RefreshToken), cancellationToken);
        return result.ToActionResult();
    }

    [Authorize(Roles = nameof(Role.Admin))]
    [HttpGet("refresh-token")]
    public async Task<ActionResult<CursorPageResult<RefreshTokenDto>>> GetAllRefreshToken(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetAllRefreshTokenQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [Authorize(Roles = nameof(Role.Admin))]
    [HttpGet("refresh-token/{refreshTokenId:guid}")]
    public async Task<ActionResult<RefreshTokenDto>> GetSpecificRefreshToken(
        Guid refreshTokenId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetSpecificRefreshTokenQuery(refreshTokenId), cancellationToken);
        return result.ToActionResult();
    }
}
