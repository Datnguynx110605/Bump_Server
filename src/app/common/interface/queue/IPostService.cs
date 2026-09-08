using FluentResults;

namespace app;

public interface IPostService
{
    Task<Result> ProcessAfterDeleteAsync(
        Guid postId,
        CancellationToken cancellationToken);
}
