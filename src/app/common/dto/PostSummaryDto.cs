using domain;

namespace app;

public sealed record PostSummaryDto(
    Guid Id,
    Guid AccountId,
    ShopSummaryDto Shop,
    List<ImageDto> Images,
    string? Caption,
    Amount Rate,
    Amount ReactionAmount,
    Amount CommentAmount,
    DateTime CreatedAt);
