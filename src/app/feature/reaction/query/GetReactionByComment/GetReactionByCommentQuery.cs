using FluentResults;
using MediatR;

namespace app;

public sealed record GetReactionByCommentQuery(
    Guid commentId,
    CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<ReactionDto>>>;
