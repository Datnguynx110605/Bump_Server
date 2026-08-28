using FluentResults;
using MediatR;

namespace app;

public sealed record DeletePostCommand(Guid postId)
    : IRequest<Result>;
