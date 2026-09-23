namespace unit;

public class NotificationTests
{
    [Fact]
    public void Create_EmptyToAccountId_IsRejected()
    {
        DomainResult<Notification> result = Notification.Create(
            Guid.Empty, "title", "content", NotificationType.Follow, Guid.NewGuid(), Guid.NewGuid(), null);

        Assert.False(result.IsSuccess);
        Assert.Equal("ToAccountId is required", result.Error);
    }

    [Fact]
    public void Create_WhitespaceTitle_IsRejected()
    {
        DomainResult<Notification> result = Notification.Create(
            Guid.NewGuid(), "   ", "content", NotificationType.Follow, Guid.NewGuid(), Guid.NewGuid(), null);

        Assert.False(result.IsSuccess);
        Assert.Equal("Title is required", result.Error);
    }

    [Fact]
    public void Create_WhitespaceContent_IsRejected()
    {
        DomainResult<Notification> result = Notification.Create(
            Guid.NewGuid(), "title", "   ", NotificationType.Follow, Guid.NewGuid(), Guid.NewGuid(), null);

        Assert.False(result.IsSuccess);
        Assert.Equal("Content is required", result.Error);
    }

    [Fact]
    public void Create_EmptyTargetId_IsRejected()
    {
        DomainResult<Notification> result = Notification.Create(
            Guid.NewGuid(), "title", "content", NotificationType.Follow, Guid.Empty, Guid.NewGuid(), null);

        Assert.False(result.IsSuccess);
        Assert.Equal("TargetId is required", result.Error);
    }

    [Fact]
    public void Create_EmptyActorAccountId_IsRejected()
    {
        DomainResult<Notification> result = Notification.Create(
            Guid.NewGuid(), "title", "content", NotificationType.Follow, Guid.NewGuid(), Guid.Empty, null);

        Assert.False(result.IsSuccess);
        Assert.Equal("ActorAccountId is required", result.Error);
    }

    [Fact]
    public void Create_WithoutActorAvatar_Succeeds()
    {
        DomainResult<Notification> result = Notification.Create(
            Guid.NewGuid(), "title", "content", NotificationType.Follow, Guid.NewGuid(), Guid.NewGuid(), null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ActorAvatar);
    }

    [Fact]
    public void Create_WithActorAvatar_Succeeds()
    {
        var avatar = new Avatar("https://avatar");

        DomainResult<Notification> result = Notification.Create(
            Guid.NewGuid(), "title", "content", NotificationType.Follow, Guid.NewGuid(), Guid.NewGuid(), avatar);

        Assert.True(result.IsSuccess);
        Assert.Same(avatar, result.Value!.ActorAvatar);
    }

    [Fact]
    public void MarkAsSent_SetsIsSentTrue()
    {
        Notification notification = Notification.Create(
            Guid.NewGuid(), "title", "content", NotificationType.Follow, Guid.NewGuid(), Guid.NewGuid(), null).Value!;

        DomainResult result = notification.MarkAsSent();

        Assert.True(result.IsSuccess);
        Assert.True(notification.IsSent);
    }
}
