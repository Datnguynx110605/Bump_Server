using FluentValidation;

namespace app;

internal sealed class ChangeCaptionValidator
    : AbstractValidator<ChangeCaptionCommand>
{
    public ChangeCaptionValidator()
    {
        RuleFor(x => x.postId)
            .NotEqual(Guid.Empty).WithMessage("PostId is required");
        RuleFor(x => x.caption)
            .NotNull().WithMessage("Caption is required")
            .MaximumLength(2000).WithMessage("Caption is too long, maximum length is 2000 characters");
    }
}
