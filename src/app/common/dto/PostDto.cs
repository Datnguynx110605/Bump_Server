using domain;

namespace app;

public sealed record PostDto(
    Guid Id,
    Guid AccountId,
    Guid ShopId,
    List<ImageDto> Images,
    string? Caption,
    Amount Rate,
    List<ReactionDto> Reactions,
    Amount ReactionAmount,
    Amount CommentAmount,
    DateTime CreatedAt);
