using FluentResults;
using MediatR;

namespace app;

public sealed record GetMyFollowingQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<FollowingDto>>>;
