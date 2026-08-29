using FluentResults;
using MediatR;

namespace app;

public sealed record GetMostReactedPostQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<PostFeedItemDto>>>;
