namespace integration;

// ICurrentIdentityService reads HttpContext in production, which handler tests never
// build. AsyncLocal rather than a plain static field so tests that fire concurrent
// Handle() calls from one method (the duplicate-key race) can still each see a stable
// value without one test's teardown racing another's setup.
public static class TestIdentity
{
    private static readonly AsyncLocal<Guid?> Holder = new();

    public static Guid? Current
    {
        get => Holder.Value;
        set => Holder.Value = value;
    }
}

// Handler tests never build an HttpContext, so they fall through to TestIdentity. An
// endpoint test sends a real bearer token through a real HTTP request, and must read the
// account id CurrentIdentityService itself would read — otherwise it would see whatever
// AsyncLocal value the previous handler test left behind rather than the token it sent.
internal sealed class FakeCurrentIdentityService : ICurrentIdentityService
{
    private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _httpContextAccessor;

    public FakeCurrentIdentityService(Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? GetAccountId()
    {
        var context = _httpContextAccessor.HttpContext;
        if(context is null)
            return TestIdentity.Current;

        var value = context.User?.FindFirst(JwtService.AccountIdClaimType)?.Value;

        return Guid.TryParse(value, out var accountId) ? accountId : null;
    }
}
