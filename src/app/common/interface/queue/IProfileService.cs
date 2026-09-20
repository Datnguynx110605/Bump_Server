using FluentResults;

namespace app;

public interface IProfileService
{
    Task<Result> ProcessProfileChangedAsync(
        Guid accountId,
        CancellationToken cancellationToken);
}
