using FluentValidation;

namespace app;

internal sealed class LoginValidator
    : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.email.Value)
            .NotEmpty().WithMessage("Email is required");
        RuleFor(x => x.password)
            .NotEmpty().WithMessage("Password is required");
    }
}
