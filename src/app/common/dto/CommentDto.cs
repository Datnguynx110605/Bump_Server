using domain;

namespace app;

public sealed record CommentDto(
    Guid Id,
    Guid AccountId,
    string? Content,
    List<ImageDto> Images,
    List<ReactionDto> Reactions,
    Amount ReactionAmount,
    DateTime CreatedAt);
