using FluentResults;
using MediatR;

namespace app;

public sealed record GetShopByAddressQuery(
    string address,
    CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<ShopSummaryDto>>>;
