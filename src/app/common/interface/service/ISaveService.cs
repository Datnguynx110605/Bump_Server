using FluentResults;

namespace app;

public interface ISaveService
{
    Task<Result> ProcessSavePostAsync(
        Guid accountId,
        Guid postId,
        CancellationToken cancellationToken);

    Task<Result> ProcessUnsavePostAsync(
        Guid accountId,
        Guid postId,
        CancellationToken cancellationToken);

    Task<Result> ProcessSaveShopAsync(
        Guid accountId,
        Guid shopId,
        CancellationToken cancellationToken);

    Task<Result> ProcessUnsaveShopAsync(
        Guid accountId,
        Guid shopId,
        CancellationToken cancellationToken);
}
