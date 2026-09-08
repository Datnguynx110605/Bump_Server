using FluentValidation;

namespace app;

internal sealed class ChangeContentValidator
    : AbstractValidator<ChangeContentCommand>
{
    public ChangeContentValidator()
    {
        RuleFor(x => x.commentId)
            .NotEqual(Guid.Empty).WithMessage("CommentId is required");
        RuleFor(x => x.content)
            .NotNull().WithMessage("Content is required")
            .MaximumLength(2000).WithMessage("Content is too long, maximum length is 2000 characters");
    }
}
