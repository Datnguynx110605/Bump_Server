using FluentResults;
using MediatR;

namespace app;

public sealed record UnreactPostCommand(Guid postId)
    : IRequest<Result>;
