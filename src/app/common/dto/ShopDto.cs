using domain;

namespace app;

public sealed record ShopDto(
    Guid Id,
    string Name,
    string Address,
    Amount Rate,
    DateTime CreatedAt);
