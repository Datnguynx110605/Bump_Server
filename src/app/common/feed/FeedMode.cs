namespace app;

// What the client is doing, and the only say it gets over paging. Refresh means "restart the
// Phase A walk from the newest data"; More means "continue whichever phase each tier's cursor
// already records". The phase itself is never chosen by the request — see FeedCursor.
public enum FeedMode
{
    Refresh,
    More
}
