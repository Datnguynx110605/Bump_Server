using domain;
using FluentResults;

namespace app;

public interface INotificationService
{
    Task<Result> CreateAsync(
        Guid toAccountId,
        string title,
        string content,
        NotificationType type,
        Guid targetId,
        Guid actorAccountId,
        Avatar? actorAvatar,
        CancellationToken cancellationToken);
}
