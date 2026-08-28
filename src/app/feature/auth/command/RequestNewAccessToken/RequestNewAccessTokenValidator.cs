using FluentValidation;

namespace app;

internal sealed class RequestNewAccessTokenValidator
    : AbstractValidator<RequestNewAccessTokenCommand>
{
    public RequestNewAccessTokenValidator()
    {
        RuleFor(x => x.refreshToken)
            .NotEmpty().WithMessage("RefreshToken is required");
    }
}
