using domain;
using MongoDB.Driver;

namespace app;

public static class CursorPaginator
{
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;

    public static async Task<CursorPageResult<T>> PaginateAsync<T>(
        IMongoCollection<T> collection,
        FilterDefinition<T> filter,
        ProjectionDefinition<T>? projection,
        CursorPageRequest request,
        CancellationToken cancellationToken)
        where T : BaseEntity
    {
        var pageSize = Math.Clamp(request.PageSize, MinPageSize, MaxPageSize);
        var cursor = CursorCodec.Decode(request.Cursor);

        var pageFilter = cursor is null
            ? filter
            : Builders<T>.Filter.And(
                filter,
                Builders<T>.Filter.Or(
                    Builders<T>.Filter.Lt(x => x.CreatedAt, cursor.Value.CreatedAt),
                    Builders<T>.Filter.And(
                        Builders<T>.Filter.Eq(x => x.CreatedAt, cursor.Value.CreatedAt),
                        Builders<T>.Filter.Lt(x => x.Id, cursor.Value.Id))));

        var find = collection
            .Find(pageFilter)
            .SortByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Limit(pageSize + 1);

        var items = projection is null
            ? await find.ToListAsync(cancellationToken)
            : await find
                .Project<T>(Builders<T>.Projection.Combine(projection, Builders<T>.Projection.Include(x => x.CreatedAt)))
                .ToListAsync(cancellationToken);

        var hasMore = items.Count > pageSize;
        var page = hasMore ? items.GetRange(0, pageSize) : items;
        var nextCursor = hasMore ? CursorCodec.Encode(page[^1].CreatedAt, page[^1].Id) : null;

        return new CursorPageResult<T>(page, nextCursor, hasMore);
    }
}
