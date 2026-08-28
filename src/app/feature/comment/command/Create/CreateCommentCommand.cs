using FluentResults;
using MediatR;

namespace app;

public sealed record CreateCommentCommand(
    Guid postId,
    string? content,
    List<(Stream stream, string fileName)>? images)
    : IRequest<Result<CommentDto>>;
