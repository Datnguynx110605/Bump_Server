using FluentValidation;

namespace app;

internal sealed class ChangeNameValidator 
    : AbstractValidator<ChangeNameCommand>
{
    public ChangeNameValidator()
    {
        RuleFor(x => x.name.Value)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(200).WithMessage("Name is too long, maximum length is 200 characters");
    }
}
