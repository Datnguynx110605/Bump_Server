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
    private Notification(
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

    public static DomainResult<Notification> Create(
        Guid toAccountId,
        string title,
        string content,
        NotificationType type,
        Guid targetId,
        Guid actorAccountId,
        Avatar? actorAvatar)
    {
        if(toAccountId == Guid.Empty)
            return DomainResult<Notification>.Failure("ToAccountId is required");

        if(string.IsNullOrWhiteSpace(title))
            return DomainResult<Notification>.Failure("Title is required");

        if(string.IsNullOrWhiteSpace(content))
            return DomainResult<Notification>.Failure("Content is required");

        if(targetId == Guid.Empty)
            return DomainResult<Notification>.Failure("TargetId is required");

        if(actorAccountId == Guid.Empty)
            return DomainResult<Notification>.Failure("ActorAccountId is required");

        Notification notification = new Notification(
            toAccountId,
            title,
            content,
            type,
            targetId,
            actorAccountId,
            actorAvatar);

        return DomainResult<Notification>.Success(notification);
    }

    public DomainResult MarkAsSent()
    {
        IsSent = true;

        return Update();
    }
}
