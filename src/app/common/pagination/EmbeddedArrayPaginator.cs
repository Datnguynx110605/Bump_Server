using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace app;

public static class EmbeddedArrayPaginator
{
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;

    public static async Task<CursorPageResult<TItem>> PaginateAsync<TParent, TItem>(
        IMongoCollection<TParent> collection,
        FilterDefinition<TParent> parentFilter,
        Expression<Func<TParent, object>> arrayField,
        Expression<Func<TItem, DateTime>> orderKey,
        Expression<Func<TItem, Guid>> tieBreaker,
        CursorPageRequest request,
        CancellationToken cancellationToken,
        IReadOnlyList<Guid>? hiddenAccountIds = null,
        Expression<Func<TItem, Guid>>? accountField = null)
    {
        var pageSize = Math.Clamp(request.PageSize, MinPageSize, MaxPageSize);
        var cursor = CursorCodec.Decode(request.Cursor);

        var arrayFieldName = GetFieldName(arrayField);
        var orderKeyPath = $"{arrayFieldName}.{GetFieldName(orderKey)}";
        var tieBreakerPath = $"{arrayFieldName}.{GetFieldName(tieBreaker)}";

        var itemFilter = cursor is null
            ? Builders<BsonDocument>.Filter.Empty
            : Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Lt(orderKeyPath, cursor.Value.CreatedAt),
                Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq(orderKeyPath, cursor.Value.CreatedAt),
                    Builders<BsonDocument>.Filter.Lt(tieBreakerPath, cursor.Value.Id)));

        // Blocked actors are dropped in the pipeline rather than from the returned page,
        // riding the item $match that already exists for the cursor. Because it runs before
        // $sort and $limit, HasMore and the cursor are computed over what the viewer can
        // actually see and pages come back full.
        if(hiddenAccountIds is { Count: > 0 } && accountField is not null)
            itemFilter = Builders<BsonDocument>.Filter.And(
                itemFilter,
                Builders<BsonDocument>.Filter.Nin($"{arrayFieldName}.{GetFieldName(accountField)}", hiddenAccountIds));

        var sort = Builders<BsonDocument>.Sort.Combine(
            Builders<BsonDocument>.Sort.Descending(orderKeyPath),
            Builders<BsonDocument>.Sort.Descending(tieBreakerPath));

        var items = await collection
            .Aggregate()
            .Match(parentFilter)
            .Project<BsonDocument>(Builders<TParent>.Projection.Include(arrayFieldName))
            .Unwind<BsonDocument>(arrayFieldName)
            .Match(itemFilter)
            .Sort(sort)
            .Limit(pageSize + 1)
            .AppendStage<TItem>(new BsonDocument("$replaceRoot", new BsonDocument("newRoot", $"${arrayFieldName}")))
            .ToListAsync(cancellationToken);

        var orderKeyGetter = orderKey.Compile();
        var tieBreakerGetter = tieBreaker.Compile();

        var hasMore = items.Count > pageSize;
        var page = hasMore ? items.GetRange(0, pageSize) : items;
        var nextCursor = hasMore
            ? CursorCodec.Encode(orderKeyGetter(page[^1]), tieBreakerGetter(page[^1]))
            : null;

        return new CursorPageResult<TItem>(page, nextCursor, hasMore);
    }

    private static string GetFieldName<TDocument, TField>(Expression<Func<TDocument, TField>> expression)
    {
        var body = expression.Body is UnaryExpression unary ? unary.Operand : expression.Body;

        return ((MemberExpression)body).Member.Name;
    }
}
