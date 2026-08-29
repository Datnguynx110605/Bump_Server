using FluentValidation;

namespace app;

internal sealed class SavePostValidator
    : AbstractValidator<SavePostCommand>
{
    public SavePostValidator()
    {
        RuleFor(x => x.postId)
            .NotEqual(Guid.Empty).WithMessage("PostId is required");
    }
}
