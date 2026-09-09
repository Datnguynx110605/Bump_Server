using FluentValidation;

namespace app;

internal sealed class BlockAccountValidator
    : AbstractValidator<BlockAccountCommand>
{
    public BlockAccountValidator()
    {
        RuleFor(x => x.accountId)
            .NotEqual(Guid.Empty).WithMessage("AccountId is required");
    }
}
