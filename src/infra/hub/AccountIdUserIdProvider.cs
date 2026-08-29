using Microsoft.AspNetCore.SignalR;

namespace infra;

public sealed class AccountIdUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.User?.FindFirst(JwtService.AccountIdClaimType)?.Value;
    }
}
