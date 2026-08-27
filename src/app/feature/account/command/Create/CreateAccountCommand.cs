using domain;
using FluentResults;
using MediatR;

namespace app;

public sealed record CreateAccountCommand(
    string registrationToken,
    Name name,
    string password,
    string? bio)
    : IRequest<Result<AccountDto>>;
