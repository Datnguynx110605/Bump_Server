using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;

namespace integration;

// test-strategy.md §5 rows 79-81, plus §3.7's cross-cutting block suite. BlockAccountHandler/
// UnblockAccountHandler check self-block synchronously (H tier, before anything is queued),
// the same pattern FollowHandler/UnfollowHandler use — unlike Reaction's handlers. The real
// write lives entirely in BlockService, the worker QueueConsumerHost drives off
// QueueName.Block. Calling the service directly (not through HTTP) is what lets the W-tier
// tests below assert on state synchronously — there is no consumer race when nothing is
// queued in the first place.
[Collection(IntegrationCollection.Name)]
public class BlockEndpointTests
{
    private readonly IntegrationTestFixture _fixture;

    public BlockEndpointTests(IntegrationTestFixture fixture)
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

    private async Task<Comment> SeedCommentAsync(Guid accountId, Guid postId)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        Comment comment = Comment.Create(accountId, postId, "hello").Value!;
        await dbContext.GetCollection<Comment>(CollectionName.Comment).InsertOneAsync(comment);

        return comment;
    }

    private async Task<Account?> FindAccountAsync(Guid id)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        return await dbContext.GetCollection<Account>(CollectionName.Account).Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    private async Task<Post?> FindPostAsync(Guid id)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        return await dbContext.GetCollection<Post>(CollectionName.Post).Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    private async Task<Comment?> FindCommentAsync(Guid id)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        return await dbContext.GetCollection<Comment>(CollectionName.Comment).Find(x => x.Id == id).FirstOrDefaultAsync();
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

    // ---------------------------------------------------------------- #79/#80 POST,DELETE /Block/{id} — E tier

    [Fact]
    public async Task BlockAccount_Self_Returns400()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Post, $"api/v1/Block/{account.Id}");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Cannot block yourself");
    }

    [Fact]
    public async Task UnblockAccount_Self_Returns400()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Block/{account.Id}");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Cannot unblock yourself");
    }

    [Fact]
    public async Task BlockAccount_Valid_Returns202_AndPublishesWithIsUndoFalse()
    {
        Account blocker = await SeedAccountAsync();
        Account blocked = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(blocker);

        var response = await SendAsync(client, HttpMethod.Post, $"api/v1/Block/{blocked.Id}");

        await AssertStatusAsync(response, HttpStatusCode.Accepted);
        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.Block &&
            m.Message is BlockMessage bm && bm.BlockerId == blocker.Id && bm.BlockedId == blocked.Id && !bm.IsUndo);
    }

    [Fact]
    public async Task UnblockAccount_Valid_Returns202_AndPublishesWithIsUndoTrue()
    {
        Account blocker = await SeedAccountAsync();
        Account blocked = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(blocker);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Block/{blocked.Id}");

        await AssertStatusAsync(response, HttpStatusCode.Accepted);
        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.Block &&
            m.Message is BlockMessage bm && bm.BlockerId == blocker.Id && bm.BlockedId == blocked.Id && bm.IsUndo);
    }

    // ---------------------------------------------------------------- BlockService — W tier

    [Fact]
    public async Task ProcessBlock_Self_Fails()
    {
        Account account = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBlockService>();

        var result = await service.ProcessBlockAsync(account.Id, account.Id, CancellationToken.None);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task ProcessBlock_TargetNotFound_Fails()
    {
        Account blocker = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBlockService>();

        var result = await service.ProcessBlockAsync(blocker.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task ProcessBlock_Valid_WritesBothArrays_SeversFollowsBothDirections_SeversReactions_DeletesBlockedSetCache()
    {
        Account blocker = await SeedAccountAsync();
        Account blocked = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();

        using var scope = _fixture.Services.CreateScope();
        var followService = scope.ServiceProvider.GetRequiredService<IFollowService>();
        var reactPostService = scope.ServiceProvider.GetRequiredService<IReactPostService>();
        var reactCommentService = scope.ServiceProvider.GetRequiredService<IReactCommentService>();
        var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();

        // Mutual follow, so both directions of severing are exercised by one block.
        await followService.ProcessFollowAsync(blocker.Id, blocked.Id, CancellationToken.None);
        await followService.ProcessFollowAsync(blocked.Id, blocker.Id, CancellationToken.None);

        // blocked reacts to blocker's post; blocker reacts to blocked's comment — one
        // reaction severed in each direction, on each content type.
        Post blockerPost = await SeedPostAsync(blocker.Id, shop.Id);
        await reactPostService.ProcessReactPostAsync(blocked.Id, blockerPost.Id, CancellationToken.None);
        Post blockedPost = await SeedPostAsync(blocked.Id, shop.Id);
        Comment blockedComment = await SeedCommentAsync(blocked.Id, blockedPost.Id);
        await reactCommentService.ProcessReactCommentAsync(blocker.Id, blockedComment.Id, CancellationToken.None);

        await redisService.SetAsync(CacheKey.BlockedSet(blocker.Id), new List<Guid>(), CacheKey.DefaultBlockedSetTTL, CancellationToken.None);
        await redisService.SetAsync(CacheKey.BlockedSet(blocked.Id), new List<Guid>(), CacheKey.DefaultBlockedSetTTL, CancellationToken.None);

        var service = scope.ServiceProvider.GetRequiredService<IBlockService>();
        var result = await service.ProcessBlockAsync(blocker.Id, blocked.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);

        Account? storedBlocker = await FindAccountAsync(blocker.Id);
        Account? storedBlocked = await FindAccountAsync(blocked.Id);
        Assert.Single(storedBlocker!.Blocks);
        Assert.Equal(blocked.Id, storedBlocker.Blocks.Single().AccountId);
        Assert.Single(storedBlocked!.BlockedBy);
        Assert.Equal(blocker.Id, storedBlocked.BlockedBy.Single());

        Assert.Empty(storedBlocker.Followers);
        Assert.Empty(storedBlocker.Followings);
        Assert.Empty(storedBlocked.Followers);
        Assert.Empty(storedBlocked.Followings);
        Assert.Equal(0, storedBlocker.FollowerAmount.Value);
        Assert.Equal(0, storedBlocker.FollowingAmount.Value);
        Assert.Equal(0, storedBlocked.FollowerAmount.Value);
        Assert.Equal(0, storedBlocked.FollowingAmount.Value);

        Post? storedBlockerPost = await FindPostAsync(blockerPost.Id);
        Assert.Empty(storedBlockerPost!.Reactions);
        Assert.Equal(0, storedBlockerPost.ReactionAmount.Value);
        Comment? storedComment = await FindCommentAsync(blockedComment.Id);
        Assert.Empty(storedComment!.Reactions);
        Assert.Equal(0, storedComment.ReactionAmount.Value);

        Assert.Null(await redisService.GetAsync<List<Guid>>(CacheKey.BlockedSet(blocker.Id), CancellationToken.None));
        Assert.Null(await redisService.GetAsync<List<Guid>>(CacheKey.BlockedSet(blocked.Id), CancellationToken.None));
    }

    // The Follow analogue of this — a pre-broken half-applied edge repaired rather than
    // throwing — is already pinned for FollowService (§5 row 51). BlockService's own comment
    // (ProcessBlockAsync, "A zero match is... deliberately NOT an exit") records the same bug
    // class was fixed here: a redelivered block whose Blocks push already landed used to stop
    // there and return Conflict, leaving the two accounts still following each other and
    // still reacting to each other's content while the block looked applied. This seeds that
    // exact half-applied state — Blocks already pushed, nothing else — and re-runs the block.
    [Fact]
    public async Task ProcessBlock_AlreadyBlocked_FallsThrough_StillSeversFollowsAndReactions_ReturnsConflict()
    {
        Account blocker = await SeedAccountAsync();
        Account blocked = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();

        using var scope = _fixture.Services.CreateScope();
        var followService = scope.ServiceProvider.GetRequiredService<IFollowService>();
        var reactPostService = scope.ServiceProvider.GetRequiredService<IReactPostService>();

        await followService.ProcessFollowAsync(blocker.Id, blocked.Id, CancellationToken.None);
        Post blockedPost = await SeedPostAsync(blocked.Id, shop.Id);
        await reactPostService.ProcessReactPostAsync(blocker.Id, blockedPost.Id, CancellationToken.None);

        // Simulates a crash between the Blocks push and the severing below: the array is
        // already there, but nothing else from a block has run yet.
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        await dbContext.GetCollection<Account>(CollectionName.Account).UpdateOneAsync(
            x => x.Id == blocker.Id,
            Builders<Account>.Update.Push(x => x.Blocks, new Block(blocked.Id, blocked.Name, null!)));

        var service = scope.ServiceProvider.GetRequiredService<IBlockService>();
        var result = await service.ProcessBlockAsync(blocker.Id, blocked.Id, CancellationToken.None);

        Assert.True(result.IsFailed);
        Assert.Equal(StatusCodes.Status409Conflict, result.Errors.Single().Metadata[AppError.StatusKey]);

        Account? storedBlocker = await FindAccountAsync(blocker.Id);
        Account? storedBlocked = await FindAccountAsync(blocked.Id);
        Assert.Single(storedBlocker!.Blocks);
        Assert.Contains(blocker.Id, storedBlocked!.BlockedBy);
        Assert.Empty(storedBlocker.Followings);
        Assert.Empty(storedBlocked.Followers);

        Post? storedPost = await FindPostAsync(blockedPost.Id);
        Assert.Empty(storedPost!.Reactions);
        Assert.Equal(0, storedPost.ReactionAmount.Value);
    }

    [Fact]
    public async Task ProcessUnblock_NotBlocked_Fails()
    {
        Account blocker = await SeedAccountAsync();
        Account blocked = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBlockService>();

        var result = await service.ProcessUnblockAsync(blocker.Id, blocked.Id, CancellationToken.None);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task ProcessUnblock_Valid_RemovesFromBlocks_DoesNotRestoreFollowsOrReactions()
    {
        Account blocker = await SeedAccountAsync();
        Account blocked = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var followService = scope.ServiceProvider.GetRequiredService<IFollowService>();
        var blockService = scope.ServiceProvider.GetRequiredService<IBlockService>();
        var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();

        await followService.ProcessFollowAsync(blocker.Id, blocked.Id, CancellationToken.None);
        await blockService.ProcessBlockAsync(blocker.Id, blocked.Id, CancellationToken.None);

        await redisService.SetAsync(CacheKey.BlockedSet(blocker.Id), new List<Guid>(), CacheKey.DefaultBlockedSetTTL, CancellationToken.None);
        await redisService.SetAsync(CacheKey.BlockedSet(blocked.Id), new List<Guid>(), CacheKey.DefaultBlockedSetTTL, CancellationToken.None);

        var result = await blockService.ProcessUnblockAsync(blocker.Id, blocked.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Account? storedBlocker = await FindAccountAsync(blocker.Id);
        Account? storedBlocked = await FindAccountAsync(blocked.Id);
        Assert.Empty(storedBlocker!.Blocks);
        Assert.Empty(storedBlocked!.BlockedBy);

        // Unblock lifts the visibility barrier and nothing else — the severed follow is
        // gone for good, the unblock does not re-friend the two accounts.
        Assert.Empty(storedBlocker.Followings);
        Assert.Empty(storedBlocked.Followers);

        Assert.Null(await redisService.GetAsync<List<Guid>>(CacheKey.BlockedSet(blocker.Id), CancellationToken.None));
        Assert.Null(await redisService.GetAsync<List<Guid>>(CacheKey.BlockedSet(blocked.Id), CancellationToken.None));
    }

    // ---------------------------------------------------------------- #81 GET /Block/me

    [Fact]
    public async Task GetMyBlockedAccount_ReturnsOwnBlockedAccountsOnly()
    {
        Account viewer = await SeedAccountAsync();
        Account blockedByViewer = await SeedAccountAsync();
        Account blockedBySomeoneElse = await SeedAccountAsync();
        Account otherBlocker = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var blockService = scope.ServiceProvider.GetRequiredService<IBlockService>();
        await blockService.ProcessBlockAsync(viewer.Id, blockedByViewer.Id, CancellationToken.None);
        await blockService.ProcessBlockAsync(otherBlocker.Id, blockedBySomeoneElse.Id, CancellationToken.None);

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Block/me");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("accountId").GetGuid()).ToList();
        Assert.Equal([blockedByViewer.Id], ids);
    }
}
