using FluentResults;

namespace app;

public interface IReactCommentService
{
    Task<Result> ProcessReactCommentAsync(
        Guid accountId,
        Guid commentId,
        CancellationToken cancellationToken);

    Task<Result> ProcessUnreactCommentAsync(
        Guid accountId,
        Guid commentId,
        CancellationToken cancellationToken);
}
