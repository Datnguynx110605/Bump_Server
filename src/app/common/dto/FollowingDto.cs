using domain;

namespace app;

public sealed record FollowingDto(
    Guid AccountId,
    Name Name,
    AvatarDto? Avatar,
    DateTime FollowedAt);
