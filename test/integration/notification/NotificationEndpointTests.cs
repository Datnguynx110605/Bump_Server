using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MongoDB.Driver;

namespace integration;

// test-strategy.md §5 rows 67-68, 82-83. RegisterDeviceHandler and UnregisterDeviceHandler
// write directly (no queue), so both are exercised through HTTP. The "delivery is covered
// once" note at the end of the Notification area applies to INotificationService.CreateAsync
// itself — called directly here rather than through one of the workers that use it
// (FollowService, ReactPostService, ReactCommentService, CommentService), since none of those
// need to re-prove the mechanics of the push.
[Collection(IntegrationCollection.Name)]
public class NotificationEndpointTests
{
    private readonly IntegrationTestFixture _fixture;

    public NotificationEndpointTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        return await client.SendAsync(request);
    }

    private static async Task AssertStatusAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string? bodyFragment = null)
    {
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"expected {expected}, got {response.StatusCode}: {body}");

        if(bodyFragment is not null)
            Assert.Contains(bodyFragment, body);
    }

    private async Task<Account> SeedAccountAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var bcryptService = scope.ServiceProvider.GetRequiredService<IBcryptService>();

        Account account = Account.Create(
            Email.Create($"acct-{Guid.NewGuid():N}@test.invalid").Value!,
            Name.Create($"acct{Guid.NewGuid():N}"[..20]).Value!,
            bcryptService.HashPassword("correct-password"),
            bio: null).Value!;

        await dbContext.GetCollection<Account>(CollectionName.Account).InsertOneAsync(account);

        return account;
    }

    private async Task<Notification> SeedNotificationAsync(Guid toAccountId)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        Notification notification = Notification.Create(
            toAccountId,
            "title",
            "content",
            NotificationType.Follow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            actorAvatar: null).Value!;

        await dbContext.GetCollection<Notification>(CollectionName.Notification).InsertOneAsync(notification);

        return notification;
    }

    private async Task<DeviceToken?> FindDeviceTokenAsync(string token)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        return await dbContext.GetCollection<DeviceToken>(CollectionName.DeviceToken)
            .Find(x => x.Token == token)
            .FirstOrDefaultAsync();
    }

    private async Task<List<Notification>> FindNotificationsForAsync(Guid toAccountId)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        return await dbContext.GetCollection<Notification>(CollectionName.Notification)
            .Find(x => x.ToAccountId == toAccountId)
            .ToListAsync();
    }

    private HttpClient AuthenticatedClientFor(Account account)
    {
        using var scope = _fixture.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtService>();
        string token = jwtService.GenerateAccessToken(account.Id, domain.Role.User);

        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    // ---------------------------------------------------------------- #82 POST /Notification/device

    [Fact]
    public async Task RegisterDevice_Valid_Returns204_AndMapsIsSandbox()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);
        string token = new string('a', 64);

        var response = await SendAsync(
            client,
            HttpMethod.Post,
            "api/v1/Notification/device",
            JsonContent.Create(new RegisterDeviceRequest(token, true)));

        await AssertStatusAsync(response, HttpStatusCode.NoContent);
        DeviceToken? stored = await FindDeviceTokenAsync(token);
        Assert.Equal(ApnsEnvironment.Sandbox, stored!.Environment);
        Assert.Equal(account.Id, stored.AccountId);
    }

    [Fact]
    public async Task RegisterDevice_IsSandboxFalse_MapsToProduction()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);
        string token = new string('b', 64);

        await SendAsync(
            client,
            HttpMethod.Post,
            "api/v1/Notification/device",
            JsonContent.Create(new RegisterDeviceRequest(token, false)));

        DeviceToken? stored = await FindDeviceTokenAsync(token);
        Assert.Equal(ApnsEnvironment.Production, stored!.Environment);
    }

    // Not an insert: RegisterDeviceHandler upserts on Token alone, so the same physical
    // device handing its token to a second account REASSIGNS the row rather than throwing
    // on the unique index a plain insert would trip.
    [Fact]
    public async Task RegisterDevice_SameTokenFromSecondAccount_ReassignsOwnership()
    {
        Account first = await SeedAccountAsync();
        Account second = await SeedAccountAsync();
        string token = new string('c', 64);

        using(var firstClient = AuthenticatedClientFor(first))
        {
            var firstResponse = await SendAsync(
                firstClient,
                HttpMethod.Post,
                "api/v1/Notification/device",
                JsonContent.Create(new RegisterDeviceRequest(token, false)));
            await AssertStatusAsync(firstResponse, HttpStatusCode.NoContent);
        }

        using var secondClient = AuthenticatedClientFor(second);
        var secondResponse = await SendAsync(
            secondClient,
            HttpMethod.Post,
            "api/v1/Notification/device",
            JsonContent.Create(new RegisterDeviceRequest(token, false)));
        await AssertStatusAsync(secondResponse, HttpStatusCode.NoContent);

        List<DeviceToken> stored;
        using(var scope = _fixture.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
            stored = await dbContext.GetCollection<DeviceToken>(CollectionName.DeviceToken)
                .Find(x => x.Token == token)
                .ToListAsync();
        }

        Assert.Single(stored);
        Assert.Equal(second.Id, stored[0].AccountId);
    }

    // A reinstall on a device APNs pruning already retired hands the same token back;
    // re-registering must revive the row rather than leave it permanently dead.
    [Fact]
    public async Task RegisterDevice_ReRegisterSoftDeletedToken_RevivesIt()
    {
        Account account = await SeedAccountAsync();
        string token = new string('d', 64);
        using var client = AuthenticatedClientFor(account);

        await SendAsync(
            client,
            HttpMethod.Post,
            "api/v1/Notification/device",
            JsonContent.Create(new RegisterDeviceRequest(token, false)));

        using(var scope = _fixture.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
            await dbContext.GetCollection<DeviceToken>(CollectionName.DeviceToken).UpdateOneAsync(
                x => x.Token == token,
                Builders<DeviceToken>.Update.Set(x => x.IsDeleted, true).Set(x => x.DeletedAt, DateTime.UtcNow));
        }

        await SendAsync(
            client,
            HttpMethod.Post,
            "api/v1/Notification/device",
            JsonContent.Create(new RegisterDeviceRequest(token, false)));

        DeviceToken? stored = await FindDeviceTokenAsync(token);
        Assert.False(stored!.IsDeleted);
        Assert.Null(stored.DeletedAt);
    }

    // ---------------------------------------------------------------- #83 DELETE /Notification/device/{token}

    [Fact]
    public async Task UnregisterDevice_Valid_SoftDeletesToken()
    {
        Account account = await SeedAccountAsync();
        string token = new string('e', 64);
        using var client = AuthenticatedClientFor(account);
        await SendAsync(
            client,
            HttpMethod.Post,
            "api/v1/Notification/device",
            JsonContent.Create(new RegisterDeviceRequest(token, false)));

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Notification/device/{token}");

        await AssertStatusAsync(response, HttpStatusCode.NoContent);
        DeviceToken? stored = await FindDeviceTokenAsync(token);
        Assert.True(stored!.IsDeleted);
    }

    // Matched on the account as well as the token, so a caller cannot retire a device that
    // isn't theirs. A miss is still success — the endpoint's own comment records that the
    // caller wanted the device to stop receiving pushes, and it already does — but the row
    // itself must be untouched.
    [Fact]
    public async Task UnregisterDevice_AnotherAccountsToken_Returns204ButLeavesRowUntouched()
    {
        Account owner = await SeedAccountAsync();
        Account attacker = await SeedAccountAsync();
        string token = new string('f', 64);
        using(var ownerClient = AuthenticatedClientFor(owner))
        {
            await SendAsync(
                ownerClient,
                HttpMethod.Post,
                "api/v1/Notification/device",
                JsonContent.Create(new RegisterDeviceRequest(token, false)));
        }

        using var attackerClient = AuthenticatedClientFor(attacker);
        var response = await SendAsync(attackerClient, HttpMethod.Delete, $"api/v1/Notification/device/{token}");

        await AssertStatusAsync(response, HttpStatusCode.NoContent);
        DeviceToken? stored = await FindDeviceTokenAsync(token);
        Assert.False(stored!.IsDeleted);
        Assert.Equal(owner.Id, stored.AccountId);
    }

    // ---------------------------------------------------------------- #67 GET /Notification/{id}

    [Fact]
    public async Task GetSpecificNotification_AnotherAccountsNotification_Returns404()
    {
        Account owner = await SeedAccountAsync();
        Account viewer = await SeedAccountAsync();
        Notification notification = await SeedNotificationAsync(owner.Id);
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Notification/{notification.Id}");

        await AssertStatusAsync(response, HttpStatusCode.NotFound);
    }

    // GetSpecificNotificationHandler checks ownership twice: once against Mongo on a cold
    // read, and again against the cached DTO on a warm one (cacheNotification.ToAccountId ==
    // identity.Value). The two are separate branches — removing the warm-path check would
    // leak a cached notification to anyone who knows its id once the owner has opened it
    // once. This populates the cache as the owner, then re-reads as a different account.
    [Fact]
    public async Task GetSpecificNotification_Owner_ThenAnotherAccountAfterCacheWarm_StillReturns404()
    {
        Account owner = await SeedAccountAsync();
        Account viewer = await SeedAccountAsync();
        Notification notification = await SeedNotificationAsync(owner.Id);

        using(var ownerClient = AuthenticatedClientFor(owner))
        {
            var ownerResponse = await SendAsync(ownerClient, HttpMethod.Get, $"api/v1/Notification/{notification.Id}");
            await AssertStatusAsync(ownerResponse, HttpStatusCode.OK);
        }

        using var viewerClient = AuthenticatedClientFor(viewer);
        var viewerResponse = await SendAsync(viewerClient, HttpMethod.Get, $"api/v1/Notification/{notification.Id}");

        await AssertStatusAsync(viewerResponse, HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------- #68 GET /Notification/mine

    [Fact]
    public async Task GetMyNotification_ReturnsOwnNotificationsOnly()
    {
        Account viewer = await SeedAccountAsync();
        Account other = await SeedAccountAsync();
        Notification mine = await SeedNotificationAsync(viewer.Id);
        await SeedNotificationAsync(other.Id);

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Notification/mine");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([mine.Id], ids);
    }

    // ---------------------------------------------------------------- Delivery — INotificationService, called once

    [Fact]
    public async Task NotificationService_Create_WritesRow_MarksSent_InvalidatesRecipientList()
    {
        Account recipient = await SeedAccountAsync();
        Account actor = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();

        long versionBefore = await redisService.GetCounterAsync(
            CacheKey.ListVersion(CollectionName.Notification, $"account:{recipient.Id}"),
            CancellationToken.None);

        var result = await service.CreateAsync(
            recipient.Id,
            "title",
            "content",
            NotificationType.Follow,
            actor.Id,
            actor.Id,
            actorAvatar: null,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        List<Notification> stored = await FindNotificationsForAsync(recipient.Id);
        Assert.Single(stored);
        Assert.True(stored[0].IsSent);

        long versionAfter = await redisService.GetCounterAsync(
            CacheKey.ListVersion(CollectionName.Notification, $"account:{recipient.Id}"),
            CancellationToken.None);
        Assert.True(versionAfter > versionBefore);
    }

    // No live device token: ApnsService's own early return ("An account with no live token
    // is the ordinary case") means this exercises the no-op path, not the swallow.
    [Fact]
    public async Task NotificationService_Create_NoDeviceToken_StillSucceeds()
    {
        Account recipient = await SeedAccountAsync();
        Account actor = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var result = await service.CreateAsync(
            recipient.Id,
            "title",
            "content",
            NotificationType.Follow,
            actor.Id,
            actor.Id,
            actorAvatar: null,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // Pins "a push failure is swallowed by design" (NotificationService's own comment) for
    // real, not just by absence of a device token. Apns:PrivateKey is now a valid PEM (needed
    // for ApnsServiceTests' HTTP-degrade-path tests, which need ApnsTokenProvider.Current() to
    // actually succeed), so this no longer forces the failure via a broken key — it forces one
    // the same way ApnsServiceTests.SendAsync_NetworkFailure_PropagatesRatherThanDegrading
    // pins ApnsService itself doing: a network failure propagates uncaught out of
    // ApnsService.SendAsync. NotificationService's try/catch around _apnsService.SendAsync is
    // what stops that exception from failing the whole notification.
    [Fact]
    public async Task NotificationService_Create_ApnsCallFails_IsSwallowed_StillSucceeds()
    {
        Account recipient = await SeedAccountAsync();
        Account actor = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        DeviceToken deviceToken = DeviceToken.Create(recipient.Id, new string('a', 64), ApnsEnvironment.Sandbox).Value!;
        await dbContext.GetCollection<DeviceToken>(CollectionName.DeviceToken).InsertOneAsync(deviceToken);
        _fixture.HttpHandler.Respond = _ => throw new HttpRequestException("simulated network failure");

        var service = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var result = await service.CreateAsync(
            recipient.Id,
            "title",
            "content",
            NotificationType.Follow,
            actor.Id,
            actor.Id,
            actorAvatar: null,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        List<Notification> stored = await FindNotificationsForAsync(recipient.Id);
        Assert.Single(stored);
        Assert.True(stored[0].IsSent);
    }
}
