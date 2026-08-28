using FluentResults;
using MediatR;

namespace app;

public sealed record UnfollowCommand(Guid followingId)
    : IRequest<Result>;
