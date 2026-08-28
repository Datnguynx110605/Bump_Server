using domain;
using FluentResults;

namespace app;

public interface IFollowService
{
    Task<Result> ProcessFollowAsync(
        Guid followerId,
        Guid followingId,
        CancellationToken cancellationToken);

    Task<Result> ProcessUnfollowAsync(
        Guid followerId,
        Guid followingId,
        CancellationToken cancellationToken);
}
