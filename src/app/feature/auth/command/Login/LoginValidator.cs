using FluentValidation;

namespace app;

internal sealed class LoginValidator
    : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.email.Value)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email is invalid")
            .MaximumLength(200).WithMessage("Email is invalid");
        RuleFor(x => x.password)
            .NotEmpty().WithMessage("Password is required");
    }
}
