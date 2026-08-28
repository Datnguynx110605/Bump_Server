using FluentValidation;

namespace app;

internal sealed class DeletePostValidator
    : AbstractValidator<DeletePostCommand>
{
    public DeletePostValidator()
    {
        RuleFor(x => x.postId)
            .NotEqual(Guid.Empty).WithMessage("PostId is required");
    }
}
