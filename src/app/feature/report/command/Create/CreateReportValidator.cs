using FluentValidation;

namespace app;

internal sealed class CreateReportValidator
    : AbstractValidator<CreateReportCommand>
{
    public CreateReportValidator()
    {
        RuleFor(x => x.reason)
            .NotEmpty().WithMessage("Reason is required")
            .MaximumLength(2000).WithMessage("Reason is too long, maximum length is 2000 characters");
        RuleFor(x => x.postId)
            .NotEqual(Guid.Empty).WithMessage("PostId is invalid")
            .When(x => x.postId is not null);
        RuleFor(x => x.commentId)
            .NotEqual(Guid.Empty).WithMessage("CommentId is invalid")
            .When(x => x.commentId is not null);
        RuleFor(x => x.accountId)
            .NotEqual(Guid.Empty).WithMessage("AccountId is invalid")
            .When(x => x.accountId is not null);
    }
}
