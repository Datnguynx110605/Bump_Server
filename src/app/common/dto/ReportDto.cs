namespace app;

public sealed record ReportDto(
    Guid Id,
    Guid ReporterId,
    Guid? PostId,
    Guid? CommentId,
    Guid? AccountId,
    string Reason,
    DateTime CreatedAt);
