namespace domain;

public abstract class BaseEntity
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; } = null;
    public DateTime? DeletedAt { get; private set; } = null;
    public bool IsDeleted { get; private set; } = false;
    public DateTime? RecoveredAt { get; private set; } = null;
    private List<Event> _events = new();
    public IReadOnlyCollection<Event> Events => _events.AsReadOnly();
    protected void RaiseEvent(Event @event) => _events.Add(@event);
    public void ClearEvent() => _events.Clear();

    public DomainResult Update()
    {
        if(IsDeleted)
            return DomainResult.Failure("Operation is invalid");
        
        UpdatedAt = DateTime.UtcNow;

        return DomainResult.Success();
    }

    public DomainResult Delete()
    {
        if(IsDeleted)
            return DomainResult.Failure("Operation is invalid");
        
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;

        return DomainResult.Success();
    }

    public DomainResult Recover()
    {
        if(!IsDeleted)
            return DomainResult.Failure("Operation is invalid");

        IsDeleted = false;
        DeletedAt = null;
        RecoveredAt = DateTime.UtcNow;

        return DomainResult.Success();
    }
}
