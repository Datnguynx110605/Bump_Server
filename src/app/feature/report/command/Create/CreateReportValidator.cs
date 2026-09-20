using FluentValidation;

namespace app;

internal sealed class CreateReportValidator
    : AbstractValidator<CreateReportCommand>
{
    public CreateReportValidator()
    {
        // A report names exactly one thing. All three targets were optional and nothing
        // required any of them, so a report about nothing validated, inserted, and sat in the
        // administrator's queue with no subject.
        RuleFor(x => x)
            .Must(x => (x.postId is not null ? 1 : 0)
                     + (x.commentId is not null ? 1 : 0)
                     + (x.accountId is not null ? 1 : 0) == 1)
            .WithMessage("A report must name exactly one of PostId, CommentId or AccountId");
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
