using FluentResults;

namespace app;

public interface ICommentService
{
    Task<Result> ProcessCreateCommentAsync(
        Guid accountId,
        Guid commentId,
        CancellationToken cancellationToken);

    Task<Result> ProcessAfterDeleteAsync(
        Guid commentId,
        CancellationToken cancellationToken);
}
