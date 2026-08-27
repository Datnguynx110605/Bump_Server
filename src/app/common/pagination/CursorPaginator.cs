using System.Globalization;
using System.Text;
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

        // CreatedAt is always merged into the projection: the cursor needs it to encode the
        // next page's starting point, even when a caller's projection doesn't ask for it.
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

    private static class CursorCodec
    {
        public static string Encode(DateTime createdAt, Guid id) =>
            EncodeBase64Url(Encoding.UTF8.GetBytes($"{createdAt:O}|{id}"));

        public static (DateTime CreatedAt, Guid Id)? Decode(string? cursor)
        {
            if (string.IsNullOrEmpty(cursor))
                return null;

            try
            {
                var raw = Encoding.UTF8.GetString(DecodeBase64Url(cursor));
                var parts = raw.Split('|');

                return (
                    DateTime.Parse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    Guid.Parse(parts[1]));
            }
            catch (FormatException)
            {
                return null;
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }
        }

        private static string EncodeBase64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] DecodeBase64Url(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');

            return Convert.FromBase64String(padded);
        }
    }
}
