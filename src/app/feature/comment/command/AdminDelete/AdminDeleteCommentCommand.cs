using FluentResults;
using MediatR;

namespace app;

public sealed record AdminDeleteCommentCommand(Guid commentId)
    : IRequest<Result>;
