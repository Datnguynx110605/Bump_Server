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

    public static TimeSpan DefaultListTTL = TimeSpan.FromMinutes(2);
    public static string List(string entity, string? filterSignature, CursorPageRequest request)
    {
        return $"list:v1:{entity}:{filterSignature ?? "all"}:{request.Cursor ?? "first"}:{request.PageSize}";
    }
}
