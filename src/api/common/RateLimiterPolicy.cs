namespace api;

public sealed record RateLimiterPolicy
{
    public const string Auth = "auth";
    public const string Email = "email";
    public const string Search = "search";
    public const string Upload = "upload";
    public const string Report = "report";
}
