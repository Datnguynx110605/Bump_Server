using FluentResults;
using MediatR;

namespace app;

public sealed record GetFollowerByAccountQuery(
    Guid accountId,
    CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<FollowerDto>>>;
