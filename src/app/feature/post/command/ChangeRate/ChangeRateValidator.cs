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
            .GreaterThanOrEqualTo(0).WithMessage("Rate must be a non-negative integer");
    }
}
