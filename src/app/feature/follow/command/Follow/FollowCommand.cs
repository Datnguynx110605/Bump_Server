using FluentResults;
using MediatR;

namespace app;

public sealed record FollowCommand(Guid followingId)
    : IRequest<Result>;
