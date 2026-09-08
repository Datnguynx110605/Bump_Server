using FluentValidation;

namespace app;

internal sealed class AdminDeleteCommentValidator
    : AbstractValidator<AdminDeleteCommentCommand>
{
    public AdminDeleteCommentValidator()
    {
        RuleFor(x => x.commentId)
            .NotEqual(Guid.Empty).WithMessage("CommentId is required");
    }
}
