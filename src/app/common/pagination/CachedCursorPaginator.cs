using System.Linq.Expressions;
using AutoMapper;
using domain;
using MongoDB.Driver;

namespace app;

public static class CachedCursorPaginator
{
    public static async Task<CursorPageResult<TDto>> PaginateAsync<TEntity, TDto>(
        IRedisService redisService,
        IMapper mapper,
        IMongoCollection<TEntity> collection,
        FilterDefinition<TEntity> filter,
        ProjectionDefinition<TEntity>? projection,
        CursorPageRequest request,
        string entity,
        string? filterSignature,
        CancellationToken cancellationToken,
        IReadOnlyList<Guid>? hiddenAccountIds = null,
        Expression<Func<TEntity, Guid>>? authorField = null)
        where TEntity : BaseEntity
    {
        // Blocked authors are excluded in Mongo rather than dropped from the page after
        // the fact, so HasMore and the cursor are computed over what the viewer can
        // actually see and pages come back full. Both arguments absent, or an empty set,
        // leaves the filter and the cache key exactly as they were.
        var pageSignature = filterSignature;

        if(hiddenAccountIds is { Count: > 0 } && authorField is not null)
        {
            filter = Builders<TEntity>.Filter.And(
                filter,
                Builders<TEntity>.Filter.Nin(authorField, hiddenAccountIds));

            pageSignature = BlockedAccountSet.Signature(filterSignature, hiddenAccountIds);
        }

        // The version counter is read under the *unsuffixed* signature, the page under the
        // suffixed one. That split is load-bearing: writers invalidate by entity and filter
        // and cannot know any viewer's hidden set, so folding the digest into the counter
        // key hides every bump from exactly the viewers who have blocked someone. One
        // counter, many page keys — a single bump retires all hidden variants at once.
        var version = await ListCache.VersionAsync(
            redisService,
            entity,
            filterSignature,
            cancellationToken);

        var cacheKey = CacheKey.List(
            entity,
            pageSignature,
            request,
            version);

        var cachedPage = await redisService.GetAsync<CursorPageResult<TDto>>(
            cacheKey,
            cancellationToken);

        if(cachedPage is not null)
            return cachedPage;

        var page = await CursorPaginator.PaginateAsync(
            collection,
            filter,
            projection,
            request,
            cancellationToken);

        var items = page.Items
            .Select(x => mapper.Map<TDto>(x))
            .ToList();

        var dtoPage = new CursorPageResult<TDto>(
            items,
            page.NextCursor,
            page.HasMore);

        await redisService.SetAsync(
            cacheKey,
            dtoPage,
            CacheKey.DefaultListTTL,
            cancellationToken);

        return dtoPage;
    }
}
