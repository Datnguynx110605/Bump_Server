using FluentValidation;

namespace app;

internal sealed class ChangeCaptionValidator
    : AbstractValidator<ChangeCaptionCommand>
{
    public ChangeCaptionValidator()
    {
        RuleFor(x => x.postId)
            .NotEqual(Guid.Empty).WithMessage("PostId is required");
    }
}
