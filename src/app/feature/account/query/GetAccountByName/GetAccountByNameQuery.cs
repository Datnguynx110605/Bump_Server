using domain;
using FluentResults;
using MediatR;

namespace app;

public sealed record GetAccountByNameQuery(
    Name name,
    CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<AccountSummaryDto>>>;
