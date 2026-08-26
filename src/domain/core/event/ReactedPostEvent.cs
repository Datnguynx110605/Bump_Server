namespace domain;

public sealed record ReactedPostEvent : Event
{
    public Guid AccountId { get; }
    public Guid PostId { get; }
    private ReactedPostEvent() {}
    public ReactedPostEvent(
        Guid accountId,
        Guid postId)
    {
        AccountId = accountId;
        PostId = postId;
    }
}  
