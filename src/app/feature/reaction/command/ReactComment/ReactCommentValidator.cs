using FluentValidation;

namespace app;

internal sealed class ReactCommentValidator
    : AbstractValidator<ReactCommentCommand>
{
    public ReactCommentValidator()
    {
        RuleFor(x => x.commentId)
            .NotEqual(Guid.Empty).WithMessage("CommentId is required");
    }
}
