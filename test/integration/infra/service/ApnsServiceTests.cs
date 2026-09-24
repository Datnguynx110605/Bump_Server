using System.Net;
using System.Net.Http.Json;
using MongoDB.Driver;

namespace integration;

[Collection(IntegrationCollection.Name)]
public class ApnsServiceTests
{
    private readonly IntegrationTestFixture _fixture;

    public ApnsServiceTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<DeviceToken> SeedDeviceTokenAsync(Guid accountId)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var collection = dbContext.GetCollection<DeviceToken>(CollectionName.DeviceToken);

        var deviceToken = DeviceToken.Create(accountId, $"device-{Guid.NewGuid():N}", ApnsEnvironment.Sandbox).Value!;
        await collection.InsertOneAsync(deviceToken);

        return deviceToken;
    }

    private async Task<DeviceToken> RereadAsync(Guid deviceTokenId)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var collection = dbContext.GetCollection<DeviceToken>(CollectionName.DeviceToken);

        return await collection.Find(x => x.Id == deviceTokenId).SingleAsync();
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string? reason = null) =>
        new(status)
        {
            Content = reason is null ? null : JsonContent.Create(new { reason })
        };

    [Fact]
    public async Task SendAsync_SuccessResponse_LeavesTheDeviceTokenUntouched()
    {
        var accountId = Guid.NewGuid();
        var deviceToken = await SeedDeviceTokenAsync(accountId);
        _fixture.HttpHandler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);

        using var scope = _fixture.Services.CreateScope();
        var apns = scope.ServiceProvider.GetRequiredService<IApnsService>();

        await apns.SendAsync(accountId, "title", "body", NotificationType.Follow, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var reread = await RereadAsync(deviceToken.Id);
        Assert.False(reread.IsDeleted);
    }

    [Fact]
    public async Task SendAsync_OrdinaryFailure_DoesNotThrow_AndLeavesTheDeviceTokenUntouched()
    {
        var accountId = Guid.NewGuid();
        var deviceToken = await SeedDeviceTokenAsync(accountId);
        _fixture.HttpHandler.Respond = _ => Response(HttpStatusCode.InternalServerError);

        using var scope = _fixture.Services.CreateScope();
        var apns = scope.ServiceProvider.GetRequiredService<IApnsService>();

        await apns.SendAsync(accountId, "title", "body", NotificationType.Follow, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var reread = await RereadAsync(deviceToken.Id);
        Assert.False(reread.IsDeleted);
    }

    [Fact]
    public async Task SendAsync_MalformedReasonBody_DoesNotThrow()
    {
        var accountId = Guid.NewGuid();
        await SeedDeviceTokenAsync(accountId);
        _fixture.HttpHandler.Respond = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("not valid json")
        };

        using var scope = _fixture.Services.CreateScope();
        var apns = scope.ServiceProvider.GetRequiredService<IApnsService>();

        await apns.SendAsync(accountId, "title", "body", NotificationType.Follow, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, null)]
    [InlineData(HttpStatusCode.BadRequest, "BadDeviceToken")]
    [InlineData(HttpStatusCode.BadRequest, "DeviceTokenNotForTopic")]
    public async Task SendAsync_DeadTokenResponse_RetiresTheDeviceToken(HttpStatusCode status, string? reason)
    {
        var accountId = Guid.NewGuid();
        var deviceToken = await SeedDeviceTokenAsync(accountId);
        _fixture.HttpHandler.Respond = _ => Response(status, reason);

        using var scope = _fixture.Services.CreateScope();
        var apns = scope.ServiceProvider.GetRequiredService<IApnsService>();

        await apns.SendAsync(accountId, "title", "body", NotificationType.Follow, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var reread = await RereadAsync(deviceToken.Id);
        Assert.True(reread.IsDeleted);
    }

    [Fact]
    public async Task SendAsync_ExpiredProviderToken_RetriesOnceWithARefreshedToken()
    {
        var accountId = Guid.NewGuid();
        await SeedDeviceTokenAsync(accountId);
        int callCount = 0;
        _fixture.HttpHandler.Respond = _ =>
        {
            callCount++;
            return callCount == 1
                ? Response(HttpStatusCode.Forbidden, "ExpiredProviderToken")
                : new HttpResponseMessage(HttpStatusCode.OK);
        };

        using var scope = _fixture.Services.CreateScope();
        var apns = scope.ServiceProvider.GetRequiredService<IApnsService>();

        await apns.SendAsync(accountId, "title", "body", NotificationType.Follow, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task SendAsync_NoDeviceTokensForAccount_ReturnsWithoutCallingApns()
    {
        bool called = false;
        _fixture.HttpHandler.Respond = _ =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        using var scope = _fixture.Services.CreateScope();
        var apns = scope.ServiceProvider.GetRequiredService<IApnsService>();

        await apns.SendAsync(Guid.NewGuid(), "title", "body", NotificationType.Follow, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.False(called);
    }

    // Not in test-strategy.md's §3.4 row for this adapter (nor should it be pinned there as
    // written — see the correction). PostAsync has no try/catch around _httpClient.SendAsync,
    // so a network failure propagates out of ApnsService.SendAsync rather than degrading to a
    // no-op here. ApnsTokenProvider's own comment says a push failure is "swallowed by design
    // in NotificationService" — that is where this degrades, not in the adapter itself.
    [Fact]
    public async Task SendAsync_NetworkFailure_PropagatesRatherThanDegrading()
    {
        var accountId = Guid.NewGuid();
        await SeedDeviceTokenAsync(accountId);
        _fixture.HttpHandler.Respond = _ => throw new HttpRequestException("simulated network failure");

        using var scope = _fixture.Services.CreateScope();
        var apns = scope.ServiceProvider.GetRequiredService<IApnsService>();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            apns.SendAsync(accountId, "title", "body", NotificationType.Follow, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }
}
