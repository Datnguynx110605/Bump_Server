using FluentValidation;

namespace app;

internal sealed class AdminDeleteAccountValidator
    : AbstractValidator<AdminDeleteAccountCommand>
{
    public AdminDeleteAccountValidator()
    {
        RuleFor(x => x.accountId)
            .NotEqual(Guid.Empty).WithMessage("AccountId is required");
    }
}
