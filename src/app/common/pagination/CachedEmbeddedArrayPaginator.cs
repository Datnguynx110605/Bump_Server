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
        string cacheKey,
        CancellationToken cancellationToken)
    {
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
