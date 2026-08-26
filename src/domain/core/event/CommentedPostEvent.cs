namespace domain;

public sealed record class CommentedPostEvent : Event
{
    public Guid AccountId { get; }
    public Guid CommentId { get; }
    public Guid PostId { get; }
    public DateTime CreatedAt { get; }
    private CommentedPostEvent() {}
    public CommentedPostEvent(
        Guid accountId,
        Guid commentId,
        Guid postId,
        DateTime createdAt)
    {
        AccountId = accountId;
        CommentId = commentId;
        PostId = postId;
        CreatedAt = createdAt;
    }
}
