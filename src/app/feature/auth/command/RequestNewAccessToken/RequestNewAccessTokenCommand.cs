using FluentResults;
using MediatR;

namespace app;

public sealed record RequestNewAccessTokenCommand(string refreshToken)
    : IRequest<Result<AuthResult>>;
