using domain;
using FluentResults;
using MediatR;

namespace app;

public sealed record LoginCommand(
    Email email,
    string password)
    : IRequest<Result<AuthResult>>;
