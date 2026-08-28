using FluentResults;
using MediatR;

namespace app;

public sealed record GetReactionByPostQuery(
    Guid postId,
    CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<ReactionDto>>>;
