namespace app;

public sealed record CursorPageResult<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);
