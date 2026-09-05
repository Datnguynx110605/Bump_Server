using FluentResults;
using MediatR;

namespace app;

public sealed record GetAllCommentQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<CommentSummaryDto>>>;
