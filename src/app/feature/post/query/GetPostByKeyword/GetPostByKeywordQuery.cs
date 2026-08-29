using FluentResults;
using MediatR;

namespace app;

public sealed record GetPostByKeywordQuery(
    string keyword,
    CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<PostFeedItemDto>>>;
