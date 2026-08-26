namespace domain;

public sealed class Reaction
{
    public Guid AccountId { get; private set; }
    public Name Name { get; private set; }
    public Avatar? Avatar { get; private set; }
    public DateTime ReactedAt { get; private set; } = DateTime.UtcNow;
    private Reaction() {}
    public Reaction(
        Guid accountId,
        Name name,
        Avatar avatar)
    {
        AccountId = accountId;
        Name = name;
        Avatar = avatar;
    }
}
