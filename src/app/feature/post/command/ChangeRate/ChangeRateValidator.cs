using FluentValidation;

namespace app;

internal sealed class ChangeRateValidator
    : AbstractValidator<ChangeRateCommand>
{
    public ChangeRateValidator()
    {
        RuleFor(x => x.postId)
            .NotEqual(Guid.Empty).WithMessage("PostId is required");
        RuleFor(x => x.rate.Value)
            .InclusiveBetween(1, 5).WithMessage("Rate must be between 1 and 5");
    }
}
