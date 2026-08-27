using FluentValidation;

namespace app;

internal sealed class ChangePasswordValidator
    : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.currentPassword)
            .NotEmpty().WithMessage("Current password is required");
        RuleFor(x => x.newPassword)
            .NotEmpty().WithMessage("New password is required")
            .MaximumLength(40).WithMessage("New password is too long, maximum length is 40 characters")
            .MinimumLength(10).WithMessage("New password is too short, minimum length is 10 characters");
    }
}
