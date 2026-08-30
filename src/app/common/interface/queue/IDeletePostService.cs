using FluentResults;

namespace app;

public interface IDeletePostService
{
    Task<Result> ProcessAfterDeleteAsync(
        Guid postId,
        CancellationToken cancellationToken);
}
