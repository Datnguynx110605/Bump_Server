using FluentResults;
using MediatR;

namespace app;

public sealed record BlockAccountCommand(Guid accountId)
    : IRequest<Result>;
