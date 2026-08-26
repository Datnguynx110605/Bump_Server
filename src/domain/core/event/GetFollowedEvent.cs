namespace domain;

public sealed record GetFollowedEvent : Event
{
    public Guid FollowerId { get; }
    public Guid FollowingId { get; }
    private GetFollowedEvent() {}
    public GetFollowedEvent(
        Guid followerId,
        Guid followingId)
    {
        FollowerId = followerId;
        FollowingId = followingId;
    }
}
