namespace domain;

public sealed class Following
{
    public Guid AccountId { get; private set; }
    public Name Name { get; private set; }
    public Avatar? Avatar { get; private set; }
    public DateTime FollowedAt { get; private set; } = DateTime.UtcNow;
    private Following() {}
    public Following(
        Guid accountId, 
        Name name, 
        Avatar avatar)
    {
        AccountId = accountId;
        Name = name;
        Avatar = avatar;
    }
}
