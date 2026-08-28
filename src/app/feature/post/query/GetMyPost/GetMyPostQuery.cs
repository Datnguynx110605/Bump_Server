using FluentResults;
using MediatR;

namespace app;

public sealed record GetMyPostQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<PostSummaryDto>>>;
