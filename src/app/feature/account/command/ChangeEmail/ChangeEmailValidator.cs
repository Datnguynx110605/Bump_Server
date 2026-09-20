using FluentValidation;

namespace app;

internal sealed class ChangeEmailValidator
    : AbstractValidator<ChangeEmailCommand>
{
    public ChangeEmailValidator()
    {
        RuleFor(x => x.registrationToken)
            .NotEmpty().WithMessage("RegistrationToken is required");
        RuleFor(x => x.currentPassword)
            .NotEmpty().WithMessage("Current password is required");
    }
}
