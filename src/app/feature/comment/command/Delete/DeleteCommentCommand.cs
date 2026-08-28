using FluentResults;
using MediatR;

namespace app;

public sealed record DeleteCommentCommand(Guid commentId)
    : IRequest<Result>;
