using FluentResults;
using MediatR;

namespace app;

internal sealed class VerifyEmailHandler
    : IRequestHandler<VerifyEmailCommand, Result<string>>
{
    private readonly IOtpService _otpService;
    private readonly IRegistrationService _registrationService;
    public VerifyEmailHandler(
        IOtpService otpService,
        IRegistrationService registrationService)
    {
        _otpService = otpService;
        _registrationService = registrationService;
    }

    public async Task<Result<string>> Handle(
        VerifyEmailCommand request, 
        CancellationToken cancellationToken)
    {
        bool verifyOtp = await _otpService.VerifyAsync(
            request.email.Value,
            request.otp,
            cancellationToken);
        if(!verifyOtp)
            return Result.Fail("Otp is incorrect");

        string registrationToken = await _registrationService.GenerateTokenAsync(
            request.email.Value,
            cancellationToken);

        return Result.Ok(registrationToken);
    }
}
