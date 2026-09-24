using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace integration;

// test-strategy.md §3.6.5: a client connecting with ?access_token= reaches NotificationHub —
// the OnMessageReceived event in AddApi exists for exactly that, since a browser/mobile
// SignalR client cannot set an Authorization header on the WebSocket/long-polling handshake —
// and a notification created for that account is delivered over it. LongPolling is forced
// because WebApplicationFactory's TestServer has no real socket to upgrade.
[Collection(IntegrationCollection.Name)]
public class NotificationHubTests
{
    private readonly IntegrationTestFixture _fixture;

    public NotificationHubTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private HubConnection BuildConnection(string? accessToken)
    {
        string url = "hubs/notification" + (accessToken is null ? "" : $"?access_token={accessToken}");

        return new HubConnectionBuilder()
            .WithUrl(new Uri(_fixture.Server.BaseAddress, url), options =>
            {
                options.HttpMessageHandlerFactory = _ => _fixture.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
    }

    [Fact]
    public async Task Connect_WithAccessTokenQueryParameter_Succeeds()
    {
        var (_, _, token) = await _fixture.CreateAuthenticatedClientAsync();
        await using var connection = BuildConnection(token);

        await connection.StartAsync();

        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task Connect_WithoutAnAccessToken_IsRejected()
    {
        await using var connection = BuildConnection(accessToken: null);

        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
    }

    [Fact]
    public async Task NotificationCreatedForAConnectedAccount_IsDeliveredOverTheHub()
    {
        var (_, account, token) = await _fixture.CreateAuthenticatedClientAsync();
        await using var connection = BuildConnection(token);

        var received = new TaskCompletionSource<JsonElement>();
        connection.On<JsonElement>("ReceiveNotification", payload => received.TrySetResult(payload));
        await connection.StartAsync();

        using var scope = _fixture.Services.CreateScope();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        Guid targetId = Guid.NewGuid();
        Guid actorId = Guid.NewGuid();

        var result = await notificationService.CreateAsync(
            account.Id,
            "New follower",
            "someone followed you",
            NotificationType.Follow,
            targetId,
            actorId,
            actorAvatar: null,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(targetId, payload.GetProperty("targetId").GetGuid());
        Assert.Equal(actorId, payload.GetProperty("actorAccountId").GetGuid());
    }
}
