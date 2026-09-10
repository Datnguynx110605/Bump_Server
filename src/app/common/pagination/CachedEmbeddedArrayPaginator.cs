using System.Linq.Expressions;
using AutoMapper;
using MongoDB.Driver;

namespace app;

public static class CachedEmbeddedArrayPaginator
{
    public static async Task<CursorPageResult<TDto>> PaginateAsync<TParent, TItem, TDto>(
        IRedisService redisService,
        IMapper mapper,
        IMongoCollection<TParent> collection,
        FilterDefinition<TParent> parentFilter,
        Expression<Func<TParent, object>> arrayField,
        Expression<Func<TItem, DateTime>> orderKey,
        Expression<Func<TItem, Guid>> tieBreaker,
        CursorPageRequest request,
        string entity,
        string? filterSignature,
        CancellationToken cancellationToken,
        IReadOnlyList<Guid>? hiddenAccountIds = null,
        Expression<Func<TItem, Guid>>? accountField = null)
    {
        var pageSignature = filterSignature;

        if(hiddenAccountIds is { Count: > 0 } && accountField is not null)
            pageSignature = BlockedAccountSet.Signature(filterSignature, hiddenAccountIds);

        // Counter under the unsuffixed signature, page under the suffixed one — see the
        // note in CachedCursorPaginator. Folding the digest into the counter key is what
        // stops a write from ever reaching a blocking viewer's cached pages.
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

        var page = await EmbeddedArrayPaginator.PaginateAsync(
            collection,
            parentFilter,
            arrayField,
            orderKey,
            tieBreaker,
            request,
            cancellationToken,
            hiddenAccountIds,
            accountField);

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
