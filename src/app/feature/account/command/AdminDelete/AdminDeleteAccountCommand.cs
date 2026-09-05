using FluentResults;
using MediatR;

namespace app;

public sealed record AdminDeleteAccountCommand(Guid accountId)
    : IRequest<Result>;
