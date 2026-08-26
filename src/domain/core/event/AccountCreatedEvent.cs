namespace domain;

public sealed record AccountCreatedEvent : Event
{
    public Guid AccountId { get; }
    public Email Email { get; }
    public DateTime CreatedAt { get; }
    private AccountCreatedEvent() {}
    public AccountCreatedEvent(
        Guid accountId,
        Email email,
        DateTime createdAt)
    {
        AccountId = accountId;
        Email = email;
        CreatedAt = createdAt;
    }
}
