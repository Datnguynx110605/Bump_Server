using domain;

namespace app;

public sealed record ShopDto(
    Guid Id,
    string Name,
    string Address,
    Amount Rate,
    DateTime CreatedAt,
    // The navigation target: an identifier opens the exact place, where a name and address
    // can only be searched for.
    string PlaceId);
