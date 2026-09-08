using FluentValidation;

namespace app;

internal sealed class SendEmailForVerifyValidator
    : AbstractValidator<SendEmailForVerifyCommand>
{
    public SendEmailForVerifyValidator()
    {
        RuleFor(x => x.email.Value)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email is invalid")
            .MaximumLength(200).WithMessage("Email is invalid");
    }
}
