using FluentValidation;

namespace app;

internal sealed class VerifyEmailValidator
    : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailValidator()
    {
        RuleFor(x => x.otp)
            .NotEmpty().WithMessage("Otp is required")
            .MinimumLength(6).WithMessage("Otp is invalid")
            .MaximumLength(6).WithMessage("Otp is invalid");
    }
}
