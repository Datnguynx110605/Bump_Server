using FluentValidation;

namespace app;

internal sealed class ChangeAvatarValidator
    : AbstractValidator<ChangeAvatarCommand>
{
    public ChangeAvatarValidator()
    {
        RuleFor(x => x.stream)
            .NotNull().WithMessage("File is required");
        RuleFor(x => x.fileName)
            .NotEmpty().WithMessage("FileName is required");
    }
}
