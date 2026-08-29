using FluentResults;

namespace app;

public interface IShopRateService
{
    Task<Result> ProcessRecalculateShopRateAsync(
        Guid shopId,
        CancellationToken cancellationToken);
}
