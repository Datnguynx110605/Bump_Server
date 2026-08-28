using FluentResults;
using MediatR;

namespace app;

public sealed record GetMyCommentHistoryQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<CommentSummaryDto>>>;
