using FluentResults;
using MediatR;

namespace app;

public sealed record GetFollowingByAccountQuery(
    Guid accountId,
    CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<FollowingDto>>>;
