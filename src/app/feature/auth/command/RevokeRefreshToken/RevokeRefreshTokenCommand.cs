using FluentResults;
using MediatR;

namespace app;

public sealed record RevokeRefreshTokenCommand(Guid refreshTokenId)
    : IRequest<Result>;
