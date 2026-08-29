using FluentResults;
using MediatR;

namespace app;

public sealed record GetMySavedShopsQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<ShopSummaryDto>>>;
