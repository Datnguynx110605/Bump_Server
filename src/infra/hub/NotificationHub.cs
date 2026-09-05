using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace infra;

[Authorize]
public sealed class NotificationHub : Hub
{
}
