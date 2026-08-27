using FluentValidation;

namespace app;

internal sealed class ChangeBioValidator
    : AbstractValidator<ChangeBioCommand>
{
    public ChangeBioValidator()
    {
        RuleFor(x => x.bio)
            .MaximumLength(2000).WithMessage("Bio is too long, maximum length is 2000 characters");
    }
}
