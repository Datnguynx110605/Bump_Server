using FluentResults;
using MediatR;

namespace app;

public sealed record GetMyBlockedAccountQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<BlockDto>>>;
