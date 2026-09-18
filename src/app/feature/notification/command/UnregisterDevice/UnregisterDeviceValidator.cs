using FluentValidation;

namespace app;

internal sealed class UnregisterDeviceValidator
    : AbstractValidator<UnregisterDeviceCommand>
{
    public UnregisterDeviceValidator()
    {
        RuleFor(x => x.token)
            .NotEmpty().WithMessage("Token is required")
            .Length(64, 200).WithMessage("Token length is invalid")
            .Matches("^[0-9a-fA-F]+$").WithMessage("Token must be hexadecimal");
    }
}
