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
        string cacheKey,
        CancellationToken cancellationToken)
        where TEntity : BaseEntity
    {
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
