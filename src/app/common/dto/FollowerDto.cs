using domain;

namespace app;

public sealed record FollowerDto(
    Guid AccountId,
    Name Name,
    AvatarDto? Avatar,
    DateTime FollowedAt);
