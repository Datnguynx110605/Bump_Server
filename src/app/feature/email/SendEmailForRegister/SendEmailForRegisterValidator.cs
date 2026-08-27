using FluentValidation;

namespace app;

internal sealed class SendEmailForRegisterValidator
    : AbstractValidator<SendEmailForRegisterCommand>
{
    public SendEmailForRegisterValidator()
    {
        RuleFor(x => x.email.Value)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email is invalid");
    }
}
