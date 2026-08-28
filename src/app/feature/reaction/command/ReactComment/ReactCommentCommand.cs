using FluentResults;
using MediatR;

namespace app;

public sealed record ReactCommentCommand(Guid commentId)
    : IRequest<Result>;
