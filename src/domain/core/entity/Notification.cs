namespace domain;

public sealed class Notification : BaseEntity
{
    public Guid ToAccountId { get; private set; }
    public string Title { get; private set; }
    public string Content { get; private set; }
    public bool IsSent { get; private set; } = false;
    public NotificationType? Type { get; private set; }
    public Guid? TargetId { get; private set; }
    public Guid? ActorAccountId { get; private set; }
    public Avatar? ActorAvatar { get; private set; }
    private Notification() {}
    public Notification(
        Guid toAccountId,
        string title,
        string content,
        NotificationType type,
        Guid targetId,
        Guid actorAccountId,
        Avatar? actorAvatar)
    {
        ToAccountId = toAccountId;
        Title = title;
        Content = content;
        Type = type;
        TargetId = targetId;
        ActorAccountId = actorAccountId;
        ActorAvatar = actorAvatar;
    }

    public DomainResult MarkAsSent()
    {
        IsSent = true;

        return Update();
    }
}
