using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;

namespace integration;

// test-strategy.md §5 rows 57-62. SavePostHandler/SaveShopHandler/UnsavePostHandler/
// UnsaveShopHandler do nothing but validate and publish — the real behaviour lives entirely
// in SaveService, the worker QueueConsumerHost drives off QueueName.SavePost/SaveShop.
// Calling the service directly (not through HTTP) is what lets the W-tier tests below assert
// on state synchronously — there is no consumer race when nothing is queued in the first place.
[Collection(IntegrationCollection.Name)]
public class SaveEndpointTests
{
    private readonly IntegrationTestFixture _fixture;

    public SaveEndpointTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path);
        return await client.SendAsync(request);
    }

    private static async Task AssertStatusAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string? caseDescription = null)
    {
        string body = await response.Content.ReadAsStringAsync();
        string prefix = caseDescription is null ? "" : $"[{caseDescription}] ";
        Assert.True(expected == response.StatusCode, $"{prefix}expected {expected}, got {response.StatusCode}: {body}");
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

    private async Task<Shop> SeedShopAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        Shop shop = Shop.Create($"Shop {Guid.NewGuid():N}", "1 Test Street").Value!;
        await dbContext.GetCollection<Shop>(CollectionName.Shop).InsertOneAsync(shop);

        return shop;
    }

    private async Task<Post> SeedPostAsync(Guid accountId, Guid shopId)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        Post post = Post.Create(accountId, shopId, "caption", Amount.Create(3).Value!).Value!;
        await dbContext.GetCollection<Post>(CollectionName.Post).InsertOneAsync(post);

        return post;
    }

    private async Task BlockAsync(Account blocker, Account blocked)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var accountCollection = dbContext.GetCollection<Account>(CollectionName.Account);

        await Task.WhenAll(
            accountCollection.UpdateOneAsync(
                x => x.Id == blocker.Id,
                Builders<Account>.Update.Push(x => x.Blocks, new Block(blocked.Id, blocked.Name, null!))),
            accountCollection.UpdateOneAsync(
                x => x.Id == blocked.Id,
                Builders<Account>.Update.Push(x => x.BlockedBy, blocker.Id)));
    }

    private async Task<Account?> FindAccountAsync(Guid id)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        return await dbContext.GetCollection<Account>(CollectionName.Account).Find(x => x.Id == id).FirstOrDefaultAsync();
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

    // ---------------------------------------------------------------- #57-60 E tier — publish only

    [Fact]
    public async Task SavePost_Valid_Returns202_AndPublishesWithIsUndoFalse()
    {
        Account account = await SeedAccountAsync();
        Guid postId = Guid.NewGuid();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Post, $"api/v1/Save/post/{postId}");

        await AssertStatusAsync(response, HttpStatusCode.Accepted);
        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.SavePost &&
            m.Message is SaveMessage sm && sm.AccountId == account.Id && sm.TargetId == postId && !sm.IsUndo);
    }

    [Fact]
    public async Task UnsavePost_Valid_Returns202_AndPublishesWithIsUndoTrue()
    {
        Account account = await SeedAccountAsync();
        Guid postId = Guid.NewGuid();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Save/post/{postId}");

        await AssertStatusAsync(response, HttpStatusCode.Accepted);
        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.SavePost &&
            m.Message is SaveMessage sm && sm.AccountId == account.Id && sm.TargetId == postId && sm.IsUndo);
    }

    [Fact]
    public async Task SaveShop_Valid_Returns202_AndPublishesWithIsUndoFalse()
    {
        Account account = await SeedAccountAsync();
        Guid shopId = Guid.NewGuid();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Post, $"api/v1/Save/shop/{shopId}");

        await AssertStatusAsync(response, HttpStatusCode.Accepted);
        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.SaveShop &&
            m.Message is SaveMessage sm && sm.AccountId == account.Id && sm.TargetId == shopId && !sm.IsUndo);
    }

    [Fact]
    public async Task UnsaveShop_Valid_Returns202_AndPublishesWithIsUndoTrue()
    {
        Account account = await SeedAccountAsync();
        Guid shopId = Guid.NewGuid();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Save/shop/{shopId}");

        await AssertStatusAsync(response, HttpStatusCode.Accepted);
        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.SaveShop &&
            m.Message is SaveMessage sm && sm.AccountId == account.Id && sm.TargetId == shopId && sm.IsUndo);
    }

    // ---------------------------------------------------------------- SaveService — W tier, posts

    [Fact]
    public async Task ProcessSavePost_PostNotFound_Fails()
    {
        Account account = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISaveService>();

        var result = await service.ProcessSavePostAsync(account.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task ProcessSavePost_Valid_PushesToSavePosts_BumpsListVersion()
    {
        Account account = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(account.Id, shop.Id);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISaveService>();
        var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();

        long versionBefore = await redisService.GetCounterAsync(
            CacheKey.ListVersion($"{CollectionName.Account}:savepost", $"account:{account.Id}"),
            CancellationToken.None);

        var result = await service.ProcessSavePostAsync(account.Id, post.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Account? stored = await FindAccountAsync(account.Id);
        Assert.Single(stored!.SavePosts);
        Assert.Equal(post.Id, stored.SavePosts.Single().PostId);

        long versionAfter = await redisService.GetCounterAsync(
            CacheKey.ListVersion($"{CollectionName.Account}:savepost", $"account:{account.Id}"),
            CancellationToken.None);
        Assert.True(versionAfter > versionBefore);
    }

    // §5 row 57's original wording called this "a no-op". It is not: ProcessSavePostAsync's
    // conditional filter excludes an id already in SavePosts, so a second save matches no
    // document, MatchedCount stays 0, and the handler answers AppError.Conflict rather than
    // silently succeeding. The array stays at exactly one entry either way — that part of
    // "no-op" was right — but the caller sees a failed Result, not Result.Ok().
    [Fact]
    public async Task ProcessSavePost_DoubleSave_ReturnsConflict_NoDuplicateEntry()
    {
        Account account = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(account.Id, shop.Id);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISaveService>();
        await service.ProcessSavePostAsync(account.Id, post.Id, CancellationToken.None);

        var second = await service.ProcessSavePostAsync(account.Id, post.Id, CancellationToken.None);

        Assert.True(second.IsFailed);
        Assert.Equal(StatusCodes.Status409Conflict, second.Errors.Single().Metadata[AppError.StatusKey]);
        Account? stored = await FindAccountAsync(account.Id);
        Assert.Single(stored!.SavePosts);
    }

    // Pins the asymmetry §5 row 5 already pins for ChangePassword: ProcessSavePostAsync's
    // failure carries AppError.Conflict (status metadata); ProcessUnsavePostAsync's "Not
    // saved this post" is a bare Result.Fail with no metadata at all, so a caller mapping
    // this over HTTP would take ResultStatusMapper's 400 default rather than a deliberate
    // status. This never reaches HTTP today — the handler only publishes — but the
    // inconsistency is real in the worker's own return value.
    [Fact]
    public async Task ProcessUnsavePost_NotSaved_FailsWithNoStatusMetadata()
    {
        Account account = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(account.Id, shop.Id);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISaveService>();

        var result = await service.ProcessUnsavePostAsync(account.Id, post.Id, CancellationToken.None);

        Assert.True(result.IsFailed);
        Assert.False(result.Errors.Single().Metadata.ContainsKey(AppError.StatusKey));
    }

    [Fact]
    public async Task ProcessUnsavePost_Valid_RemovesFromSavePosts_BumpsListVersion()
    {
        Account account = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(account.Id, shop.Id);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISaveService>();
        var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();
        await service.ProcessSavePostAsync(account.Id, post.Id, CancellationToken.None);

        long versionBefore = await redisService.GetCounterAsync(
            CacheKey.ListVersion($"{CollectionName.Account}:savepost", $"account:{account.Id}"),
            CancellationToken.None);

        var result = await service.ProcessUnsavePostAsync(account.Id, post.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Account? stored = await FindAccountAsync(account.Id);
        Assert.Empty(stored!.SavePosts);

        long versionAfter = await redisService.GetCounterAsync(
            CacheKey.ListVersion($"{CollectionName.Account}:savepost", $"account:{account.Id}"),
            CancellationToken.None);
        Assert.True(versionAfter > versionBefore);
    }

    // ---------------------------------------------------------------- SaveService — W tier, shops

    [Fact]
    public async Task ProcessSaveShop_ShopNotFound_Fails()
    {
        Account account = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISaveService>();

        var result = await service.ProcessSaveShopAsync(account.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task ProcessSaveShop_Valid_PushesToSaveShops()
    {
        Account account = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISaveService>();

        var result = await service.ProcessSaveShopAsync(account.Id, shop.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Account? stored = await FindAccountAsync(account.Id);
        Assert.Single(stored!.SaveShops);
        Assert.Equal(shop.Id, stored.SaveShops.Single().ShopId);
    }

    [Fact]
    public async Task ProcessSaveShop_DoubleSave_ReturnsConflict_NoDuplicateEntry()
    {
        Account account = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISaveService>();
        await service.ProcessSaveShopAsync(account.Id, shop.Id, CancellationToken.None);

        var second = await service.ProcessSaveShopAsync(account.Id, shop.Id, CancellationToken.None);

        Assert.True(second.IsFailed);
        Account? stored = await FindAccountAsync(account.Id);
        Assert.Single(stored!.SaveShops);
    }

    [Fact]
    public async Task ProcessUnsaveShop_Valid_RemovesFromSaveShops()
    {
        Account account = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISaveService>();
        await service.ProcessSaveShopAsync(account.Id, shop.Id, CancellationToken.None);

        var result = await service.ProcessUnsaveShopAsync(account.Id, shop.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Account? stored = await FindAccountAsync(account.Id);
        Assert.Empty(stored!.SaveShops);
    }

    // ---------------------------------------------------------------- #61 GET /Save/me/post

    [Fact]
    public async Task GetMySavedPosts_Pagination_ReturnsFullPageAndCursor()
    {
        Account viewer = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();

        using var scope = _fixture.Services.CreateScope();
        var saveService = scope.ServiceProvider.GetRequiredService<ISaveService>();

        var savedIds = new List<Guid>();
        for(int i = 0; i < 21; i++)
        {
            Post post = await SeedPostAsync(author.Id, shop.Id);
            await saveService.ProcessSavePostAsync(viewer.Id, post.Id, CancellationToken.None);
            savedIds.Add(post.Id);
        }

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Save/me/post?pageSize=20");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(page.GetProperty("hasMore").GetBoolean());
        Assert.False(page.GetProperty("nextCursor").ValueKind == JsonValueKind.Null);
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(20, ids.Count);
        Assert.All(ids, id => Assert.Contains(id, savedIds));
    }

    // Corrects §5 row 61 and §3.7's exception list: GetMySavedPostsHandler's own comment
    // (SavePost holds only PostId/SavedAt, never the author) states the $nin filtering it
    // to blocked authors cannot ride the same paginating query EmbeddedArrayPaginator already
    // used to compute HasMore/cursor over the full saved set. So unlike every other filtered
    // read in §3.7, a page here comes back SHORT when a saved post's author is blocked,
    // rather than backfilled to stay full.
    [Fact]
    public async Task GetMySavedPosts_BlockedAuthor_ShortensPage_NotBackfilled()
    {
        Account viewer = await SeedAccountAsync();
        Account visibleAuthor = await SeedAccountAsync();
        Account blockedAuthor = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        await BlockAsync(viewer, blockedAuthor);

        using var scope = _fixture.Services.CreateScope();
        var saveService = scope.ServiceProvider.GetRequiredService<ISaveService>();

        Post visiblePost = await SeedPostAsync(visibleAuthor.Id, shop.Id);
        Post blockedPost = await SeedPostAsync(blockedAuthor.Id, shop.Id);
        await saveService.ProcessSavePostAsync(viewer.Id, visiblePost.Id, CancellationToken.None);
        await saveService.ProcessSavePostAsync(viewer.Id, blockedPost.Id, CancellationToken.None);

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Save/me/post?pageSize=20");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(page.GetProperty("hasMore").GetBoolean());
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();

        // Both saves landed in the same one-page window (HasMore is false), so a full page
        // would report 2. It reports 1: the blocked author's post was pushed out of the
        // second stage entirely, not replaced by the next id in line.
        Assert.Single(ids);
        Assert.Contains(visiblePost.Id, ids);
        Assert.DoesNotContain(blockedPost.Id, ids);
    }

    // ---------------------------------------------------------------- #62 GET /Save/me/shop

    [Fact]
    public async Task GetMySavedShops_Pagination_ReturnsFullPageAndCursor()
    {
        Account viewer = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var saveService = scope.ServiceProvider.GetRequiredService<ISaveService>();

        var savedIds = new List<Guid>();
        for(int i = 0; i < 21; i++)
        {
            Shop shop = await SeedShopAsync();
            await saveService.ProcessSaveShopAsync(viewer.Id, shop.Id, CancellationToken.None);
            savedIds.Add(shop.Id);
        }

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Save/me/shop?pageSize=20");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(page.GetProperty("hasMore").GetBoolean());
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(20, ids.Count);
        Assert.All(ids, id => Assert.Contains(id, savedIds));
    }
}
