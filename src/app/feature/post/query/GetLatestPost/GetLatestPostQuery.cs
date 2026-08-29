using FluentResults;
using MediatR;

namespace app;

public sealed record GetLatestPostQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<PostFeedItemDto>>>;
