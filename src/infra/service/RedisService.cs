using System.Text.Json;
using app;
using StackExchange.Redis;

namespace infra;

public sealed class RedisService : IRedisService
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;

    public RedisService(IConnectionMultiplexer connectionMultiplexer)
    {
        _connectionMultiplexer = connectionMultiplexer;
    }

    public async Task SetAsync<T>(
        string key,
        T value, 
        TimeSpan? ttl, 
        CancellationToken cancellationToken)
    {
        var db = _connectionMultiplexer.GetDatabase();
        var expiration = ttl.HasValue ? (Expiration)ttl.Value : Expiration.Default;

        await db.StringSetAsync(
            key, 
            JsonSerializer.Serialize(value, DomainJsonOptions.Options), 
            expiration);
    }

    public async Task<T?> GetAsync<T>(
        string key, 
        CancellationToken cancellationToken)
    {
        var db = _connectionMultiplexer.GetDatabase();
        var value = await db.StringGetAsync(key);

        return value.HasValue ? JsonSerializer.Deserialize<T>(value!, DomainJsonOptions.Options) : default;
    }

    public async Task<T?> GetAndRemoveAsync<T>(
        string key, 
        CancellationToken cancellationToken)
    {
        var db = _connectionMultiplexer.GetDatabase();
        var value = await db.StringGetDeleteAsync(key);

        return value.HasValue ? JsonSerializer.Deserialize<T>(value!, DomainJsonOptions.Options) : default;
    }

    public async Task RemoveAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var db = _connectionMultiplexer.GetDatabase();

        await db.KeyDeleteAsync(key);
    }

    // INCR creates a missing key with no expiry, so without this the counters would
    // outlive every page they version and accumulate one key per entity and signature
    // forever. The TTL slides on each write and must stay well above DefaultListTTL:
    // if a counter expired back to 0 while a page key from that epoch was still alive,
    // the stale page would be served again.
    public async Task<long> IncrementAsync(
        string key,
        TimeSpan? ttl,
        CancellationToken cancellationToken)
    {
        var db = _connectionMultiplexer.GetDatabase();

        var value = await db.StringIncrementAsync(key);

        if(ttl.HasValue)
            await db.KeyExpireAsync(key, ttl.Value);

        return value;
    }

    // Counters are stored by Redis as raw integers rather than as the JSON the other
    // members read and write, so this deliberately bypasses the serializer. A missing
    // key means nothing has been invalidated yet, which is version 0.
    public async Task<long> GetCounterAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var db = _connectionMultiplexer.GetDatabase();
        var value = await db.StringGetAsync(key);

        return value.TryParse(out long counter) ? counter : 0;
    }
}
