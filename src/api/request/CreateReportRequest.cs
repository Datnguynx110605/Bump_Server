namespace api;

public sealed record CreateReportRequest(
    Guid? PostId,
    Guid? CommentId,
    Guid? AccountId,
    string Reason);
