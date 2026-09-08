using FluentResults;

namespace app;

public interface IAccountService
{
    Task<Result> ProcessAfterDeleteAsync(
        Guid accountId,
        CancellationToken cancellationToken);
}
