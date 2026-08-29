using FluentResults;

namespace app;

public interface INotificationService
{
    Task<Result> CreateAsync(
        Guid toAccountId,
        string title,
        string content,
        CancellationToken cancellationToken);
}
