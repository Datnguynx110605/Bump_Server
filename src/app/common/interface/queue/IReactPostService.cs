using FluentResults;

namespace app;

public interface IReactPostService
{
    Task<Result> ProcessReactPostAsync(
        Guid accountId,
        Guid postId,
        CancellationToken cancellationToken);

    Task<Result> ProcessUnreactPostAsync(
        Guid accountId,
        Guid postId,
        CancellationToken cancellationToken);
}
