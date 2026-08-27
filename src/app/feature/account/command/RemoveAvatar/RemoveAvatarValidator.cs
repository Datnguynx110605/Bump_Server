using FluentValidation;

namespace app;

internal sealed class RemoveAvatarValidator
    : AbstractValidator<RemoveAvatarCommand>
{
    public RemoveAvatarValidator()
    {
        RuleFor(x => x.url)
            .NotEmpty().WithMessage("Url is required");
    }
}
