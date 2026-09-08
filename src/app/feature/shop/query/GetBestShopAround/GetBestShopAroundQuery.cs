using FluentResults;
using MediatR;

namespace app;

public sealed record GetBestShopAroundQuery(double latitude, double longitude)
    : IRequest<Result<CursorPageResult<ShopSummaryDto>>>;
