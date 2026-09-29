using domain;
using FluentResults;
using MediatR;

namespace app;

// A place identifier and nothing else about the shop. The identifier is resolved against the
// maps provider and the shop is whatever that answers: name, address and coordinate all come
// back from the provider, so there is no shop text or coordinate here for a client to supply.
// A pair supplied by the client is indistinguishable from the position of the person writing
// the review rather than of the shop being reviewed, and a name is only trustworthy when it
// arrives under an identifier the client cannot invent.
public sealed record CreatePostCommand(
    string placeId,
    string? caption,
    Amount rate,
    List<(Stream stream, string fileName)>? images)
    : IRequest<Result<PostDto>>;
