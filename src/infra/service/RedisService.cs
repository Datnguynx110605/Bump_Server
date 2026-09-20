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

        cancellationToken.ThrowIfCancellationRequested();

        await db.StringSetAsync(
            key, 
            JsonSerializer.Serialize(value, DomainJsonOptions.Options), 
            expiration);
    }

    public async Task<T?> GetAsync<T>(
        string key, 
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var db = _connectionMultiplexer.GetDatabase();
        var value = await db.StringGetAsync(key);

        return value.HasValue ? JsonSerializer.Deserialize<T>(value!, DomainJsonOptions.Options) : default;
    }

    public async Task<T?> GetAndRemoveAsync<T>(
        string key, 
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var db = _connectionMultiplexer.GetDatabase();
        var value = await db.StringGetDeleteAsync(key);

        return value.HasValue ? JsonSerializer.Deserialize<T>(value!, DomainJsonOptions.Options) : default;
    }

    public async Task RemoveAsync(
        string key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var db = _connectionMultiplexer.GetDatabase();

        await db.KeyDeleteAsync(key);
    }

    // INCR creates a missing key with no expiry, so without this the counters would
    // outlive every page they version and accumulate one key per entity and signature
    // forever. The TTL slides on each write and must stay well above DefaultListTTL:
    // if a counter expired back to 0 while a page key from that epoch was still alive,
    // the stale page would be served again.
    // One server round trip rather than two. The INCR and the EXPIRE were separate awaits,
    // which is cheap where the caller batches its invalidations into a Task.WhenAll and is
    // not where it awaits them one at a time — and the pair was never atomic either, so a
    // process dying between them left a counter with no TTL, exactly the leak the TTL is
    // there to prevent.
    private const string IncrementWithTtlScript =
        "local v = redis.call('INCR', KEYS[1]) " +
        "if ARGV[1] ~= '' then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end " +
        "return v";

    public async Task<long> IncrementAsync(
        string key,
        TimeSpan? ttl,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var db = _connectionMultiplexer.GetDatabase();

        var value = await db.ScriptEvaluateAsync(
            IncrementWithTtlScript,
            new RedisKey[] { key },
            new RedisValue[] { ttl.HasValue ? (long)ttl.Value.TotalMilliseconds : string.Empty });

        return (long)value;
    }

    // Counters are stored by Redis as raw integers rather than as the JSON the other
    // members read and write, so this deliberately bypasses the serializer. A missing
    // key means nothing has been invalidated yet, which is version 0.
    public async Task<long> GetCounterAsync(
        string key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        cancellationToken.ThrowIfCancellationRequested();

        var db = _connectionMultiplexer.GetDatabase();
        var value = await db.StringGetAsync(key);

        return value.TryParse(out long counter) ? counter : 0;
    }
}
