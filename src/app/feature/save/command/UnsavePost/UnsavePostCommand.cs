using FluentResults;
using MediatR;

namespace app;

public sealed record UnsavePostCommand(Guid postId)
    : IRequest<Result>;
