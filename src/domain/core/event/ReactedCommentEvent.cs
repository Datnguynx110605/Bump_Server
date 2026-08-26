namespace domain;

public sealed record ReactedCommentEvent : Event
{
    public Guid AccountId { get; }
    public Guid CommentId { get; }
    private ReactedCommentEvent() {}
    public ReactedCommentEvent(
        Guid accountId,
        Guid commentId)
    {
        AccountId = accountId;
        CommentId = commentId;
    }
}  
