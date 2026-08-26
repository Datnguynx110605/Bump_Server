using domain;

namespace app;

public sealed record PostDto(
    Guid Id,
    Guid AccountId,
    ShopSummaryDto Shop,
    List<ImageDto> Images,
    string? Caption,
    Amount Rate,
    List<ReactionDto> Reactions,
    Amount ReactionAmount,
    List<CommentSummaryDto> Comments,
    Amount CommentAmount,
    DateTime CreatedAt);
