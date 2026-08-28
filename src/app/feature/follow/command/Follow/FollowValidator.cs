using FluentValidation;

namespace app;

internal sealed class FollowValidator
    : AbstractValidator<FollowCommand>
{
    public FollowValidator()
    {
        RuleFor(x => x.followingId)
            .NotEqual(Guid.Empty).WithMessage("FollowingId is required");
    }
}
