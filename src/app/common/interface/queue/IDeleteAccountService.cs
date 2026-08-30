using FluentResults;

namespace app;

public interface IDeleteAccountService
{
    Task<Result> ProcessAfterDeleteAsync(
        Guid accountId,
        CancellationToken cancellationToken);
}
