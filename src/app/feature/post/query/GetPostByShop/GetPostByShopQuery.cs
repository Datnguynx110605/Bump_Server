using FluentResults;
using MediatR;

namespace app;

public sealed record GetPostByShopQuery(
    Guid shopId,
    CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<PostSummaryDto>>>;
