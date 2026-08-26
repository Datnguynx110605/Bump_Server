using domain;

namespace app;

public sealed record ReactionDto(
    Guid AccountId,
    Name Name,
    AvatarDto? Avatar,
    DateTime ReactedAt);
