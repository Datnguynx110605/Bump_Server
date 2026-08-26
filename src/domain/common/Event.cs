namespace domain;

public abstract record Event
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EntityId { get; private set; }
    public string PayloadHash { get; private set; }
    public EventStatus Status { get; private set; } = EventStatus.Pending;
    public DateTime RaisedAt { get; private set; } = DateTime.UtcNow;
}
