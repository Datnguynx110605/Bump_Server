using FluentValidation;

namespace app;

internal sealed class RegisterDeviceValidator
    : AbstractValidator<RegisterDeviceCommand>
{
    public RegisterDeviceValidator()
    {
        // The token is interpolated straight into the APNs request path, so this is a trust
        // boundary. Real tokens are 64 hex characters today; the upper bound is generous
        // because Apple reserves the right to lengthen them.
        RuleFor(x => x.token)
            .NotEmpty().WithMessage("Token is required")
            .Length(64, 200).WithMessage("Token length is invalid")
            .Matches("^[0-9a-fA-F]+$").WithMessage("Token must be hexadecimal");
    }
}
