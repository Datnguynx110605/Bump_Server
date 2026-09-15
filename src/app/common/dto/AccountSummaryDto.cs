using domain;

namespace app;

public sealed record AccountSummaryDto(
    Guid Id,
    Name Name,
    AvatarDto? Avatar,
    string? Bio,
    Amount FollowerAmount,
    Amount FollowingAmount,
    bool IsTrusted);
