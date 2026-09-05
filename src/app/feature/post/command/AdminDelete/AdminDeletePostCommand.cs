using FluentResults;
using MediatR;

namespace app;

public sealed record AdminDeletePostCommand(Guid postId)
    : IRequest<Result>;
