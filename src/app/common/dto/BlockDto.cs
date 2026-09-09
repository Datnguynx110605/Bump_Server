using domain;

namespace app;

public sealed record BlockDto(
    Guid AccountId,
    Name Name,
    AvatarDto? Avatar,
    DateTime BlockedAt);
