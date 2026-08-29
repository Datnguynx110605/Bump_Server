namespace domain;

public sealed class Notification : BaseEntity
{
    public Guid ToAccountId { get; private set; }
    public string Title { get; private set; }
    public string Content { get; private set; }
    public bool IsSent { get; private set; } = false;
    private Notification() {}
    public Notification(
        Guid toAccountId,
        string title,
        string content)
    {
        ToAccountId = toAccountId;
        Title = title;
        Content = content;
    }

    public DomainResult MarkAsSent()
    {
        IsSent = true;

        return Update();
    }
}
