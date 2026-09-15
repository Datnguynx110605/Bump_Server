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
public sealed class AccountController : ControllerBase
{
    private readonly ISender _sender;

    public AccountController(ISender sender)
    {
        _sender = sender;
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimiterPolicy.Auth)]
    [HttpPost]
    public async Task<ActionResult<AccountDto>> CreateAccount(
        CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var nameResult = Name.Create(request.Name);
        if(!nameResult.IsSuccess)
            return Result.Fail<AccountDto>(nameResult.Error!).ToActionResult();

        var result = await _sender.Send(new CreateAccountCommand(request.RegistrationToken, nameResult.Value!, request.Password, request.Bio), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("me")]
    public async Task<ActionResult<AccountSummaryDto>> GetMyAccount(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetMyAccountQuery(), cancellationToken);
        return result.ToActionResult();
    }

    [HttpPut("me/name")]
    public async Task<ActionResult> ChangeName(
        ChangeNameRequest request,
        CancellationToken cancellationToken)
    {
        var nameResult = Name.Create(request.Name);
        if(!nameResult.IsSuccess)
            return Result.Fail(nameResult.Error!).ToActionResult();

        var result = await _sender.Send(new ChangeNameCommand(nameResult.Value!), cancellationToken);
        return result.ToActionResult();
    }

    [HttpPut("me/bio")]
    public async Task<ActionResult> ChangeBio(
        ChangeBioRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ChangeBioCommand(request.Bio), cancellationToken);
        return result.ToActionResult();
    }

    // Answers 200 with a fresh token pair rather than 204: the handler revokes every
    // refresh token on the account, so the caller's own session needs replacing.
    [HttpPut("me/password")]
    public async Task<ActionResult<AuthResult>> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ChangePasswordCommand(request.CurrentPassword, request.NewPassword), cancellationToken);
        return result.ToActionResult();
    }

    [HttpPut("me/email")]
    public async Task<ActionResult> ChangeEmail(
        ChangeEmailRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ChangeEmailCommand(request.RegistrationToken), cancellationToken);
        return result.ToActionResult();
    }

    [EnableRateLimiting(RateLimiterPolicy.Upload)]
    [HttpPost("me/avatar")]
    public async Task<ActionResult> ChangeAvatar(
        IFormFile avatar,
        CancellationToken cancellationToken)
    {
        var validationResult = ImageUpload.Validate(avatar);
        if(validationResult.IsFailed)
            return validationResult.ToActionResult();

        var result = await _sender.Send(new ChangeAvatarCommand(avatar.OpenReadStream(), avatar.FileName), cancellationToken);
        return result.ToActionResult();
    }

    [HttpDelete("me/avatar")]
    public async Task<ActionResult> RemoveAvatar(
        [FromQuery] string url,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new RemoveAvatarCommand(url), cancellationToken);
        return result.ToActionResult();
    }

    [HttpDelete("me")]
    public async Task<ActionResult> DeleteAccount(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new DeleteAccountCommand(), cancellationToken);
        return result.ToActionResult();
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimiterPolicy.Auth)]
    [HttpPost("recover")]
    public async Task<ActionResult> RecoverAccount(
        RecoverAccountRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new RecoverAccountCommand(request.RegistrationToken), cancellationToken);
        return result.ToActionResult();
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimiterPolicy.Auth)]
    [HttpPut("reset-password")]
    public async Task<ActionResult> ResetPassword(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ResetPasswordCommand(request.RegistrationToken, request.NewPassword), cancellationToken);
        return result.ToActionResult();
    }

    [Authorize(Roles = nameof(Role.Admin))]
    [HttpGet]
    public async Task<ActionResult<CursorPageResult<AccountSummaryDto>>> GetAllAccount(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetAllAccountQuery(new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("{accountId:guid}")]
    public async Task<ActionResult<AccountSummaryDto>> GetSpecificAccount(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetSpecificAccountQuery(accountId), cancellationToken);
        return result.ToActionResult();
    }

    [Authorize(Roles = nameof(Role.Admin))]
    [HttpGet("by-email")]
    public async Task<ActionResult<AccountSummaryDto>> GetAccountByEmail(
        [FromQuery] string email,
        CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(email);
        if(!emailResult.IsSuccess)
            return Result.Fail<AccountSummaryDto>(emailResult.Error!).ToActionResult();

        var result = await _sender.Send(new GetAccountByEmailQuery(emailResult.Value!), cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("by-name")]
    public async Task<ActionResult<CursorPageResult<AccountSummaryDto>>> GetAccountByName(
        [FromQuery] string name,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var nameResult = Name.Create(name);
        if(!nameResult.IsSuccess)
            return Result.Fail<CursorPageResult<AccountSummaryDto>>(nameResult.Error!).ToActionResult();

        var result = await _sender.Send(new GetAccountByNameQuery(nameResult.Value!, new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [EnableRateLimiting(RateLimiterPolicy.Search)]
    [HttpGet("search")]
    public async Task<ActionResult<CursorPageResult<AccountSummaryDto>>> GetAccountByKeyword(
        [FromQuery] string keyword,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetAccountByKeywordQuery(keyword, new CursorPageRequest(cursor, pageSize)), cancellationToken);
        return result.ToActionResult();
    }

    [Authorize(Roles = nameof(Role.Admin))]
    [HttpDelete("admin/{accountId:guid}")]
    public async Task<ActionResult> AdminDeleteAccount(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new AdminDeleteAccountCommand(accountId), cancellationToken);
        return result.ToActionResult();
    }
}
