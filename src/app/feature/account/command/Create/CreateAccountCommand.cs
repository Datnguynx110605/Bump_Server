using domain;
using FluentResults;
using MediatR;

namespace app;

public sealed record CreateAccountCommand(
    string registrationToken,
    Name name,
    Stream? image,
    string imageFileName,
    string password,
    string? bio)
    : IRequest<Result<AccountDto>>;
