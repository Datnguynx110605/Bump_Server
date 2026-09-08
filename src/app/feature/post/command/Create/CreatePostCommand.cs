using domain;
using FluentResults;
using MediatR;

namespace app;

public sealed record CreatePostCommand(
    string shopName,
    string shopAddress,
    double? latitude,
    double? longitude,
    string? caption,
    Amount rate,
    List<(Stream stream, string fileName)>? images)
    : IRequest<Result<PostDto>>;
