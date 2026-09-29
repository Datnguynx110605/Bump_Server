using FluentResults;
using MediatR;

namespace app;

public sealed record GetRelatedPostQuery(
    FeedMode mode,
    double? latitude,
    double? longitude,
    CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<PostFeedItemDto>>>;
