using FluentResults;
using MediatR;

namespace app;

public sealed record GetAllRefreshTokenQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<RefreshTokenDto>>>;
