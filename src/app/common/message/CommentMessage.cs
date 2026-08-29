namespace app;

public sealed record CommentMessage(
    Guid MessageId,
    Guid AccountId,
    Guid CommentId);
