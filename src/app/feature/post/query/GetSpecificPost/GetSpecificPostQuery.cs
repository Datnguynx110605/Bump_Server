using FluentResults;
using MediatR;

namespace app;

public sealed record GetSpecificPostQuery(Guid postId)
    : IRequest<Result<PostFeedItemDto>>;
