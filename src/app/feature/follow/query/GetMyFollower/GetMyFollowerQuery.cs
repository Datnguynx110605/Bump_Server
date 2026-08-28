using FluentResults;
using MediatR;

namespace app;

public sealed record GetMyFollowerQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<FollowerDto>>>;
