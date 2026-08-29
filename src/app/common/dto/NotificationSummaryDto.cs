namespace app;

public sealed record NotificationSummaryDto(
    Guid Id,
    Guid ToAccountId,
    string Title,
    string Content,
    bool IsSent,
    DateTime CreatedAt);
