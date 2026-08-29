using FluentValidation;

namespace app;

internal sealed class UnsavePostValidator
    : AbstractValidator<UnsavePostCommand>
{
    public UnsavePostValidator()
    {
        RuleFor(x => x.postId)
            .NotEqual(Guid.Empty).WithMessage("PostId is required");
    }
}
