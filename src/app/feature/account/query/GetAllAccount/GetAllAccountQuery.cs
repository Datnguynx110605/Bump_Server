using FluentResults;
using MediatR;

namespace app;

public sealed record GetAllAccountQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<AccountSummaryDto>>>;
