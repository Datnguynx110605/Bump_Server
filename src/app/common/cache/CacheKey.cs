using domain;

namespace app;

public sealed record CacheKey
{
    public static TimeSpan DefaultAccountTTL = TimeSpan.FromDays(7);
    public static string Account(Guid accountId)
    {
        return $"account:v1:id:{accountId}";
    }

    public static string AccountByEmail(Email email)
    {
        return $"account:v1:email:{email.Value}";
    }

    public static TimeSpan DefaultPostTTL = TimeSpan.FromDays(7);
    public static string Post(Guid postId)
    {
        return $"post:v1:id:{postId}";
    }

    public static TimeSpan DefaultCommentTTL = TimeSpan.FromDays(7);
    public static string Comment(Guid commentId)
    {
        return $"comment:v1:id:{commentId}";
    }

    public static TimeSpan DefaultReportTTL = TimeSpan.FromDays(7);
    public static string Report(Guid reportId)
    {
        return $"report:v1:id:{reportId}";
    }

    public static TimeSpan DefaultShopTTL = TimeSpan.FromDays(7);
    public static string Shop(Guid shopId)
    {
        return $"shop:v1:id:{shopId}";
    }

    public static TimeSpan DefaultNotificationTTL = TimeSpan.FromDays(7);
    public static string Notification(Guid notificationId)
    {
        return $"notification:v1:id:{notificationId}";
    }

    public static TimeSpan DefaultListTTL = TimeSpan.FromMinutes(2);

    // The version suffix is what makes invalidation complete. Deleting keys directly only
    // ever cleared cursor=first/pageSize=20, leaving every other page and page size stale
    // until the TTL; bumping the version retires all of them at once.
    public static string List(string entity, string? filterSignature, CursorPageRequest request, long version)
    {
        return $"list:v1:{entity}:{filterSignature ?? "all"}:{request.Cursor ?? "first"}:{request.PageSize}:v{version}";
    }

    // Must stay comfortably above DefaultListTTL so a counter can never expire back to 0
    // while a page key stamped with the current version is still cached.
    public static TimeSpan ListVersionTTL = TimeSpan.FromDays(1);
    public static string ListVersion(string entity, string? filterSignature)
    {
        return $"list-version:{entity}:{filterSignature ?? "all"}";
    }
}
