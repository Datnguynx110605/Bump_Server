using FluentResults;
using MediatR;

namespace app;

public sealed record GetShopByKeywordQuery(
    string keyword,
    CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<ShopSummaryDto>>>;
