using FluentResults;
using MediatR;

namespace app;

public sealed record GetAccountByKeywordQuery(
    string keyword,
    CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<AccountSummaryDto>>>;
