using FluentResults;

namespace app;

public interface IBlockService
{
    Task<Result> ProcessBlockAsync(
        Guid blockerId,
        Guid blockedId,
        CancellationToken cancellationToken);

    Task<Result> ProcessUnblockAsync(
        Guid blockerId,
        Guid blockedId,
        CancellationToken cancellationToken);
}
