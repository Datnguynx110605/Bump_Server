using app;
using Microsoft.AspNetCore.Http;

namespace infra;

public class CurrentIdentityService : ICurrentIdentityService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentIdentityService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? GetAccountId()
    {
        var value = _httpContextAccessor.HttpContext?.User?.FindFirst(JwtService.AccountIdClaimType)?.Value;

        return Guid.TryParse(value, out var accountId) ? accountId : null;
    }
}
