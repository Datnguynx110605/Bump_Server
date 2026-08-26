namespace domain;

public sealed class Follower
{
    public Guid AccountId { get; private set; }
    public Name Name { get; private set; }
    public Avatar? Avatar { get; private set; }
    public DateTime FollowedAt { get; private set; } = DateTime.UtcNow;
    private Follower() {}
    public Follower(
        Guid accountId, 
        Name name, 
        Avatar avatar)
    {
        AccountId = accountId;
        Name = name;
        Avatar = avatar;
    }
}
