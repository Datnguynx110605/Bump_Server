using FluentResults;

namespace app;

public interface IReactionService
{
    Task<Result> ProcessReactPostAsync(
        Guid accountId,
        Guid postId,
        CancellationToken cancellationToken);

    Task<Result> ProcessUnreactPostAsync(
        Guid accountId,
        Guid postId,
        CancellationToken cancellationToken);

    Task<Result> ProcessReactCommentAsync(
        Guid accountId,
        Guid commentId,
        CancellationToken cancellationToken);

    Task<Result> ProcessUnreactCommentAsync(
        Guid accountId,
        Guid commentId,
        CancellationToken cancellationToken);
}
