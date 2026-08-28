using FluentValidation;

namespace app;

internal sealed class UnreactCommentValidator
    : AbstractValidator<UnreactCommentCommand>
{
    public UnreactCommentValidator()
    {
        RuleFor(x => x.commentId)
            .NotEqual(Guid.Empty).WithMessage("CommentId is required");
    }
}
