using FluentResults;
using MediatR;

namespace app;

public sealed record UnblockAccountCommand(Guid accountId)
    : IRequest<Result>;
