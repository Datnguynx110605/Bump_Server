namespace app;

// Cached list pages are retired by bumping a version counter rather than by deleting
// keys. Every invalidation site used to delete exactly one key shape
// (cursor=first, pageSize=20), so any other page or page size served stale data until
// its TTL expired. Granularity is unchanged: the counter is per entity and filter
// signature, the same pair the delete calls targeted.
public static class ListCache
{
    public static Task<long> VersionAsync(
        IRedisService redisService,
        string entity,
        string? filterSignature,
        CancellationToken cancellationToken)
    {
        return redisService.GetCounterAsync(
            CacheKey.ListVersion(entity, filterSignature),
            cancellationToken);
    }

    public static Task InvalidateAsync(
        IRedisService redisService,
        string entity,
        string? filterSignature,
        CancellationToken cancellationToken)
    {
        return redisService.IncrementAsync(
            CacheKey.ListVersion(entity, filterSignature),
            CacheKey.ListVersionTTL,
            cancellationToken);
    }
}
