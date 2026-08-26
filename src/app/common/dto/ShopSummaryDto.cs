using domain;

namespace app;

public sealed record ShopSummaryDto(
    Guid Id,
    string Name,
    string Address,
    Amount Rate);
