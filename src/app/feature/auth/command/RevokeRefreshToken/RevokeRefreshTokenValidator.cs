using FluentValidation;

namespace app;

internal sealed class RevokeRefreshTokenValidator
    : AbstractValidator<RevokeRefreshTokenCommand>
{
    public RevokeRefreshTokenValidator()
    {
        RuleFor(x => x.refreshTokenId)
            .NotEqual(Guid.Empty).WithMessage("RefreshTokenId is required");
    }
}
