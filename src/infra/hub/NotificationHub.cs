using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace infra;

[Authorize]
public sealed class NotificationHub : Hub
{
    private readonly IConnectionMultiplexer _redis;
    public NotificationHub(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public override async Task OnConnectedAsync()
    {
        await _redis.GetDatabase().StringIncrementAsync(OnlineKey(Context.UserIdentifier));

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _redis.GetDatabase().StringDecrementAsync(OnlineKey(Context.UserIdentifier));

        await base.OnDisconnectedAsync(exception);
    }

    internal static string OnlineKey(string? accountId) => $"notification:online:{accountId}";
}
