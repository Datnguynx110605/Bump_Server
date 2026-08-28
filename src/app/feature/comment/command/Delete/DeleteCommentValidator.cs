using FluentValidation;

namespace app;

internal sealed class DeleteCommentValidator
    : AbstractValidator<DeleteCommentCommand>
{
    public DeleteCommentValidator()
    {
        RuleFor(x => x.commentId)
            .NotEqual(Guid.Empty).WithMessage("CommentId is required");
    }
}
