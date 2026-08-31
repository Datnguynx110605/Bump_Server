using FluentValidation;

namespace app;

internal sealed class RecoverAccountValidator
    : AbstractValidator<RecoverAccountCommand>
{
    public RecoverAccountValidator()
    {
        RuleFor(x => x.registrationToken)
            .NotEmpty().WithMessage("RegistrationToken is required");
    }
}
