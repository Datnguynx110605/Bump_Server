using FluentValidation;

namespace app;

internal sealed class AdminDeletePostValidator
    : AbstractValidator<AdminDeletePostCommand>
{
    public AdminDeletePostValidator()
    {
        RuleFor(x => x.postId)
            .NotEqual(Guid.Empty).WithMessage("PostId is required");
    }
}
