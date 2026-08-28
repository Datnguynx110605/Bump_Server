using FluentResults;
using MediatR;

namespace app;

public sealed record GetSpecificRefreshTokenQuery(Guid refreshTokenId)
    : IRequest<Result<RefreshTokenDto>>;
