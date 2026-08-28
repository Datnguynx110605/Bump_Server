using FluentValidation;

namespace app;

internal sealed class UnfollowValidator
    : AbstractValidator<UnfollowCommand>
{
    public UnfollowValidator()
    {
        RuleFor(x => x.followingId)
            .NotEqual(Guid.Empty).WithMessage("FollowingId is required");
    }
}
