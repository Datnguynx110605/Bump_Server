using domain;

namespace app;

public sealed record CommentSummaryDto(
    Guid Id,
    Guid AccountId,
    string? Content,
    List<ImageDto> Images,
    Amount ReactionAmount,
    DateTime CreatedAt);
