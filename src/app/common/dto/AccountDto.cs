using domain;

namespace app;

public sealed record AccountDto(
    Guid Id,
    Email Email,
    Name Name,
    List<AvatarDto> Avatars,
    string? Bio,
    Amount FollowerAmount,
    Amount FollowingAmount,
    DateTime CreatedAt);
