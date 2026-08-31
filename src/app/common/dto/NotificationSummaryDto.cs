using domain;

namespace app;

public sealed record NotificationSummaryDto(
    Guid Id,
    Guid ToAccountId,
    string Title,
    string Content,
    bool IsSent,
    DateTime CreatedAt,
    NotificationType? Type,
    Guid? TargetId,
    Guid? ActorAccountId,
    AvatarDto? ActorAvatar);
