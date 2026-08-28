using FluentResults;
using MediatR;

namespace app;

public sealed record GetAllShopQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<ShopSummaryDto>>>;
