using FluentValidation;

namespace app;

internal sealed class GetReportByTargetValidator
    : AbstractValidator<GetReportByTargetQuery>
{
    public GetReportByTargetValidator()
    {
        RuleFor(x => x.target)
            .IsInEnum().WithMessage("Target is invalid");
        RuleFor(x => x.targetId)
            .NotEqual(Guid.Empty).WithMessage("TargetId is required");
    }
}
