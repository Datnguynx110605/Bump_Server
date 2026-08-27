using FluentValidation;

namespace app;

internal sealed class CreateAccountValidator 
    : AbstractValidator<CreateAccountCommand>
{
    public CreateAccountValidator()
    {
        RuleFor(x => x.registrationToken)
            .NotEmpty().WithMessage("RegistrationToken is required");
        RuleFor(x => x.password)
            .NotEmpty().WithMessage("Password is required")
            .MaximumLength(40).WithMessage("Password is too long, maximum length is 40 characters")
            .MinimumLength(10).WithMessage("Password is too short, minimum length is 10 characters");
        RuleFor(x => x.bio)
            .MaximumLength(2000).WithMessage("Bio is too long, maximum length is 2000 characters");
        RuleFor(x => x.name.Value)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(200).WithMessage("Name is too long, maximum length is 200 characters");
    }
}
