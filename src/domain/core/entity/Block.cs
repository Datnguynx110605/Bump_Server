namespace domain;

public sealed class Block
{
    public Guid AccountId { get; private set; }
    public Name Name { get; private set; }
    public Avatar? Avatar { get; private set; }
    public DateTime BlockedAt { get; private set; } = DateTime.UtcNow;
    private Block() {}
    public Block(
        Guid accountId, 
        Name name, 
        Avatar avatar)
    {
        AccountId = accountId;
        Name = name;
        Avatar = avatar;
    }
}
