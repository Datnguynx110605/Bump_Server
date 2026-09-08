using FluentResults;
using MediatR;

namespace app;

public sealed record GetAllPostQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<PostSummaryDto>>>;
