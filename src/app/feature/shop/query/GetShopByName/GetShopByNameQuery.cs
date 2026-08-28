using FluentResults;
using MediatR;

namespace app;

public sealed record GetShopByNameQuery(
    string name,
    CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<ShopSummaryDto>>>;
