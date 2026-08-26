namespace app;

public interface IRedisService
{
    Task SetAsync<T>(string key, T value, TimeSpan? ttl, CancellationToken cancellationToken);

    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken);

    Task RemoveAsync(string key, CancellationToken cancellationToken);
}
