namespace app;

// PageSize is clamped here rather than only inside the paginators, because CacheKey.List
// builds the page key from this value and used to see the raw one. `?pageSize=999999` and
// `?pageSize=100` returned identical content under different keys, so every distinct value
// was a guaranteed cache miss, a fresh Mongo query and a new key — one caller could mint
// unbounded cache keys and bypass the list cache while staying inside the rate limits.
//
// The paginators still clamp; that is now defence in depth rather than the only guard.
public sealed record CursorPageRequest(string? Cursor, int PageSize = 20)
{
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;

    public int PageSize { get; } = Math.Clamp(PageSize, MinPageSize, MaxPageSize);
}
