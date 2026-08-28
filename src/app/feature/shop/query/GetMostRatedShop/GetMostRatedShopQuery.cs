using FluentResults;
using MediatR;

namespace app;

public sealed record GetMostRatedShopQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<ShopSummaryDto>>>;
