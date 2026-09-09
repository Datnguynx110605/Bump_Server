using FluentValidation;

namespace app;

internal sealed class UnblockAccountValidator
    : AbstractValidator<UnblockAccountCommand>
{
    public UnblockAccountValidator()
    {
        RuleFor(x => x.accountId)
            .NotEqual(Guid.Empty).WithMessage("AccountId is required");
    }
}
