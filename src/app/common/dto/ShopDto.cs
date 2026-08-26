using domain;

namespace app;

public sealed record ShopDto(
    Guid Id,
    string Name,
    string Address,
    Amount Rate,
    List<PostSummaryDto> Posts,
    DateTime CreatedAt);
