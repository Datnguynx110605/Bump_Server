using FluentResults;
using MediatR;

namespace app;

public sealed record GetRelatedPostQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<PostFeedItemDto>>>;
