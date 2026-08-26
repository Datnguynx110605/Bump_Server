namespace domain;

public sealed record PostCreatedEvent : Event
{
    public Guid AccountId { get; }
    public Guid PostId { get; }
    public DateTime CreatedAt { get; }
    private PostCreatedEvent() {}
    public PostCreatedEvent(
        Guid accountId,
        Guid postId,
        DateTime createdAt)
    {
        AccountId = accountId;
        PostId = postId;
        CreatedAt = createdAt;
    }
}
