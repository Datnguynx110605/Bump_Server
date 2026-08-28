using FluentResults;
using MediatR;

namespace app;

public sealed record GetCommentByPostQuery(
    Guid postId,
    CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<CommentSummaryDto>>>;
