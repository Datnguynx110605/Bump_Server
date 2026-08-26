namespace app;

public sealed record CursorPageRequest(string? Cursor, int PageSize = 20);
