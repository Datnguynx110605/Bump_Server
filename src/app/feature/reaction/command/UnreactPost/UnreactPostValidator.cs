using FluentValidation;

namespace app;

internal sealed class UnreactPostValidator
    : AbstractValidator<UnreactPostCommand>
{
    public UnreactPostValidator()
    {
        RuleFor(x => x.postId)
            .NotEqual(Guid.Empty).WithMessage("PostId is required");
    }
}
