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
public sealed class EmailController : ControllerBase
{
    private readonly ISender _sender;

    public EmailController(ISender sender)
    {
        _sender = sender;
    }

    [AllowAnonymous]
    [HttpPost("send-for-register")]
    public async Task<ActionResult> SendForRegister(
        SendEmailRequest request,
        CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(request.Email);
        if(!emailResult.IsSuccess)
            return Result.Fail(emailResult.Error!).ToActionResult();

        var result = await _sender.Send(new SendEmailForRegisterCommand(emailResult.Value!), cancellationToken);
        return result.ToActionResult();
    }

    [AllowAnonymous]
    [HttpPost("send-for-verify")]
    public async Task<ActionResult> SendForVerify(
        SendEmailRequest request,
        CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(request.Email);
        if(!emailResult.IsSuccess)
            return Result.Fail(emailResult.Error!).ToActionResult();

        var result = await _sender.Send(new SendEmailForVerifyCommand(emailResult.Value!), cancellationToken);
        return result.ToActionResult();
    }

    [AllowAnonymous]
    [HttpPost("verify")]
    public async Task<ActionResult<string>> Verify(
        VerifyEmailRequest request,
        CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(request.Email);
        if(!emailResult.IsSuccess)
            return Result.Fail<string>(emailResult.Error!).ToActionResult();

        var result = await _sender.Send(new VerifyEmailCommand(emailResult.Value!, request.Otp), cancellationToken);
        return result.ToActionResult();
    }
}
