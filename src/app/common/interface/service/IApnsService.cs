using domain;

namespace app;

public interface IApnsService
{
    Task SendAsync(
        Guid toAccountId,
        string title,
        string body,
        NotificationType type,
        Guid targetId,
        Guid notificationId,
        CancellationToken cancellationToken);
}
