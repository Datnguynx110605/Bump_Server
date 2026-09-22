using domain;
using FluentResults;
using MediatR;

namespace app;

// Either a place identifier or a typed shop, never neither. A post naming a place is
// verified against the maps provider and the shop text on it is ignored; a post without
// one creates the unverified shop the client typed, which is the tier that keeps a street
// cart the provider has never heard of postable.
public sealed record CreatePostCommand(
    string? placeId,
    string? shopName,
    string? shopAddress,
    double? latitude,
    double? longitude,
    string? caption,
    Amount rate,
    List<(Stream stream, string fileName)>? images)
    : IRequest<Result<PostDto>>;
