using FluentValidation;

namespace app;

internal sealed class CreateReportValidator
    : AbstractValidator<CreateReportCommand>
{
    public CreateReportValidator()
    {
        RuleFor(x => x.reason)
            .NotEmpty().WithMessage("Reason is required");
    }
}
