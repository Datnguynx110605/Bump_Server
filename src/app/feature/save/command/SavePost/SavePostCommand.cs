using FluentResults;
using MediatR;

namespace app;

public sealed record SavePostCommand(Guid postId)
    : IRequest<Result>;
