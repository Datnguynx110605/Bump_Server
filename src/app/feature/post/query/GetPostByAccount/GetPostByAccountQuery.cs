using FluentResults;
using MediatR;

namespace app;

public sealed record GetPostByAccountQuery(
    Guid accountId,
    CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<PostSummaryDto>>>;
