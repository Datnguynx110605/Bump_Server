using domain;

namespace app;

public sealed record ShopDto(
    Guid Id,
    string Name,
    string Address,
    Amount Rate,
    DateTime CreatedAt,
    // Present only on a shop resolved from the maps provider. The client reads it as both
    // the verification badge and the navigation target: an identifier opens the exact
    // place, where a name and address can only be searched for.
    string? PlaceId);
