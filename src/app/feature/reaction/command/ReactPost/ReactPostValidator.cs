using FluentValidation;

namespace app;

internal sealed class ReactPostValidator
    : AbstractValidator<ReactPostCommand>
{
    public ReactPostValidator()
    {
        RuleFor(x => x.postId)
            .NotEqual(Guid.Empty).WithMessage("PostId is required");
    }
}
