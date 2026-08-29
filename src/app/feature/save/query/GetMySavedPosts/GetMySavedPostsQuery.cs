using FluentResults;
using MediatR;

namespace app;

public sealed record GetMySavedPostsQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<PostSummaryDto>>>;
