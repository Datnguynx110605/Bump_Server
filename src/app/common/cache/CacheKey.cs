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
    public static string List(string entity, string? filterSignature, CursorPageRequest request)
    {
        return $"list:v1:{entity}:{filterSignature ?? "all"}:{request.Cursor ?? "first"}:{request.PageSize}";
    }
}
