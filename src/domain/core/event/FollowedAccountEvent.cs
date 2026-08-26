namespace domain;

public sealed record FollowedAccountEvent : Event
{
    public Guid FollowerId { get; }
    public Guid FollowingId { get; }
    private FollowedAccountEvent() {}
    public FollowedAccountEvent(
        Guid followerId,
        Guid followingId)
    {
        FollowerId = followerId;
        FollowingId = followingId;
    }
}
