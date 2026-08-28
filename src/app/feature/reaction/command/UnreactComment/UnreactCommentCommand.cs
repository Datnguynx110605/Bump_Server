using FluentResults;
using MediatR;

namespace app;

public sealed record UnreactCommentCommand(Guid commentId)
    : IRequest<Result>;
