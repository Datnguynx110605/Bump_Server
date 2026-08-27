using System.Text.Json;
using app;
using StackExchange.Redis;

namespace infra;

internal sealed class RedisService : IRedisService
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;

    public RedisService(IConnectionMultiplexer connectionMultiplexer)
    {
        _connectionMultiplexer = connectionMultiplexer;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl, CancellationToken cancellationToken)
    {
        var db = _connectionMultiplexer.GetDatabase();
        var expiration = ttl.HasValue ? (Expiration)ttl.Value : Expiration.Default;

        await db.StringSetAsync(key, JsonSerializer.Serialize(value, DomainJsonOptions.Options), expiration);
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        var db = _connectionMultiplexer.GetDatabase();
        var value = await db.StringGetAsync(key);

        return value.HasValue ? JsonSerializer.Deserialize<T>(value!, DomainJsonOptions.Options) : default;
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        var db = _connectionMultiplexer.GetDatabase();

        await db.KeyDeleteAsync(key);
    }
}
