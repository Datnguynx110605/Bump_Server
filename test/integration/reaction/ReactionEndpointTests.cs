using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MongoDB.Driver;

namespace integration;

// test-strategy.md §5 rows 45-50. ReactPostHandler/UnreactPostHandler (and their Comment
// counterparts) do nothing but validate and publish — the real behaviour (existence
// checks, the block guard, the push/pull and the counter, the double-react/double-unreact
// invariants, the notification) lives entirely in ReactPostService/ReactCommentService,
// the workers QueueConsumerHost drives off QueueName.ReactPost/ReactComment. Calling the
// service directly (not through HTTP) is what lets the W-tier tests below assert on state
// synchronously — there is no consumer race when nothing is queued in the first place.
[Collection(IntegrationCollection.Name)]
public class ReactionEndpointTests
{
    private readonly IntegrationTestFixture _fixture;

    public ReactionEndpointTests(IntegrationTestFixture fixture)
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

        Shop shop = Shop.Create($"Shop {Guid.NewGuid():N}", "1 Test Street", 0, 0, $"place-{Guid.NewGuid():N}").Value!;
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

    // Writes both arrays, like the real Block worker does (§5 row 79): BlockGuard.IsBlockedAsync
    // reads only the actor's own document (its Blocks OR its BlockedBy), so a one-sided write
    // here would leave the guard blind whenever the reacting account is the one that got blocked
    // rather than the one who did the blocking.
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

    // ---------------------------------------------------------------- #45/#46 POST,DELETE /Reaction/post/{id} — E tier

    [Fact]
    public async Task ReactPost_Valid_Returns202_AndPublishesWithIsUndoFalse()
    {
        Account actor = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        using var client = AuthenticatedClientFor(actor);

        var response = await SendAsync(client, HttpMethod.Post, $"api/v1/Reaction/post/{post.Id}");

        await AssertStatusAsync(response, HttpStatusCode.Accepted);
        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.ReactPost &&
            m.Message is ReactionMessage rm && rm.TargetId == post.Id && rm.AccountId == actor.Id && !rm.IsUndo);
    }

    [Fact]
    public async Task UnreactPost_Valid_Returns202_AndPublishesWithIsUndoTrue()
    {
        Account actor = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        using var client = AuthenticatedClientFor(actor);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Reaction/post/{post.Id}");

        await AssertStatusAsync(response, HttpStatusCode.Accepted);
        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.ReactPost &&
            m.Message is ReactionMessage rm && rm.TargetId == post.Id && rm.AccountId == actor.Id && rm.IsUndo);
    }

    // ---------------------------------------------------------------- #47/#48 POST,DELETE /Reaction/comment/{id} — E tier

    [Fact]
    public async Task ReactComment_Valid_Returns202_AndPublishesWithIsUndoFalse()
    {
        Account actor = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        Comment comment = await SeedCommentAsync(author.Id, post.Id);
        using var client = AuthenticatedClientFor(actor);

        var response = await SendAsync(client, HttpMethod.Post, $"api/v1/Reaction/comment/{comment.Id}");

        await AssertStatusAsync(response, HttpStatusCode.Accepted);
        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.ReactComment &&
            m.Message is ReactionMessage rm && rm.TargetId == comment.Id && rm.AccountId == actor.Id && !rm.IsUndo);
    }

    [Fact]
    public async Task UnreactComment_Valid_Returns202_AndPublishesWithIsUndoTrue()
    {
        Account actor = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        Comment comment = await SeedCommentAsync(author.Id, post.Id);
        using var client = AuthenticatedClientFor(actor);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Reaction/comment/{comment.Id}");

        await AssertStatusAsync(response, HttpStatusCode.Accepted);
        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.ReactComment &&
            m.Message is ReactionMessage rm && rm.TargetId == comment.Id && rm.AccountId == actor.Id && rm.IsUndo);
    }

    // ---------------------------------------------------------------- ReactPostService — W tier

    [Fact]
    public async Task ProcessReactPost_AccountNotFound_Fails()
    {
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReactPostService>();

        var result = await service.ProcessReactPostAsync(Guid.NewGuid(), post.Id, CancellationToken.None);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task ProcessReactPost_PostNotFound_Fails()
    {
        Account actor = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReactPostService>();

        var result = await service.ProcessReactPostAsync(actor.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task ProcessReactPost_BlockedByPostAuthor_FailsSilently_NoReactionPushed()
    {
        Account actor = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        await BlockAsync(author, actor);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReactPostService>();

        var result = await service.ProcessReactPostAsync(actor.Id, post.Id, CancellationToken.None);

        Assert.True(result.IsFailed);
        Post? stored = await FindPostAsync(post.Id);
        Assert.Empty(stored!.Reactions);
        Assert.Equal(0, stored.ReactionAmount.Value);
    }

    [Fact]
    public async Task ProcessReactPost_Valid_PushesReaction_IncrementsCount_CreatesNotification()
    {
        Account actor = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReactPostService>();

        var result = await service.ProcessReactPostAsync(actor.Id, post.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Post? stored = await FindPostAsync(post.Id);
        Assert.Single(stored!.Reactions);
        Assert.Equal(actor.Id, stored.Reactions.Single().AccountId);
        Assert.Equal(1, stored.ReactionAmount.Value);

        var notifications = await FindNotificationsForAsync(author.Id);
        Assert.Single(notifications);
        Assert.Equal(NotificationType.PostReaction, notifications[0].Type);
    }

    [Fact]
    public async Task ProcessReactPost_SelfReact_NoNotificationCreated()
    {
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReactPostService>();

        var result = await service.ProcessReactPostAsync(author.Id, post.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var notifications = await FindNotificationsForAsync(author.Id);
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task ProcessReactPost_DoubleReact_ReturnsConflict_CounterNotDoubleIncremented()
    {
        Account actor = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReactPostService>();

        var first = await service.ProcessReactPostAsync(actor.Id, post.Id, CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await service.ProcessReactPostAsync(actor.Id, post.Id, CancellationToken.None);
        Assert.True(second.IsFailed);

        Post? stored = await FindPostAsync(post.Id);
        Assert.Single(stored!.Reactions);
        Assert.Equal(1, stored.ReactionAmount.Value);
    }

    [Fact]
    public async Task ProcessUnreactPost_Valid_PullsReaction_DecrementsCount()
    {
        Account actor = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReactPostService>();
        await service.ProcessReactPostAsync(actor.Id, post.Id, CancellationToken.None);

        var result = await service.ProcessUnreactPostAsync(actor.Id, post.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Post? stored = await FindPostAsync(post.Id);
        Assert.Empty(stored!.Reactions);
        Assert.Equal(0, stored.ReactionAmount.Value);
    }

    // Pins §5 row 46: the second unreact finds no matching document (the filter requires
    // the account still be present in Reactions), so it no-ops rather than driving the
    // counter negative. The failure is real but silent — the endpoint already answered 202.
    [Fact]
    public async Task ProcessUnreactPost_DoubleUnreact_LeavesCounterAtZero_NeverNegative()
    {
        Account actor = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReactPostService>();
        await service.ProcessReactPostAsync(actor.Id, post.Id, CancellationToken.None);
        await service.ProcessUnreactPostAsync(actor.Id, post.Id, CancellationToken.None);

        var second = await service.ProcessUnreactPostAsync(actor.Id, post.Id, CancellationToken.None);

        Assert.True(second.IsFailed);
        Post? stored = await FindPostAsync(post.Id);
        Assert.Equal(0, stored!.ReactionAmount.Value);
    }

    // ---------------------------------------------------------------- ReactCommentService — W tier

    [Fact]
    public async Task ProcessReactComment_Valid_PushesReaction_IncrementsCount_CreatesNotification()
    {
        Account actor = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        Comment comment = await SeedCommentAsync(author.Id, post.Id);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReactCommentService>();

        var result = await service.ProcessReactCommentAsync(actor.Id, comment.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Comment? stored = await FindCommentAsync(comment.Id);
        Assert.Single(stored!.Reactions);
        Assert.Equal(1, stored.ReactionAmount.Value);

        var notifications = await FindNotificationsForAsync(author.Id);
        Assert.Single(notifications);
        Assert.Equal(NotificationType.CommentReaction, notifications[0].Type);
    }

    [Fact]
    public async Task ProcessReactComment_BlockedByCommentAuthor_FailsSilently()
    {
        Account actor = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        Comment comment = await SeedCommentAsync(author.Id, post.Id);
        await BlockAsync(author, actor);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReactCommentService>();

        var result = await service.ProcessReactCommentAsync(actor.Id, comment.Id, CancellationToken.None);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task ProcessReactComment_DoubleReact_ReturnsConflict()
    {
        Account actor = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        Comment comment = await SeedCommentAsync(author.Id, post.Id);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReactCommentService>();
        await service.ProcessReactCommentAsync(actor.Id, comment.Id, CancellationToken.None);

        var second = await service.ProcessReactCommentAsync(actor.Id, comment.Id, CancellationToken.None);

        Assert.True(second.IsFailed);
        Comment? stored = await FindCommentAsync(comment.Id);
        Assert.Single(stored!.Reactions);
    }

    // Pins §5 row 48, the same invariant as row 46 on the comment side.
    [Fact]
    public async Task ProcessUnreactComment_DoubleUnreact_LeavesCounterAtZero_NeverNegative()
    {
        Account actor = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        Comment comment = await SeedCommentAsync(author.Id, post.Id);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReactCommentService>();
        await service.ProcessReactCommentAsync(actor.Id, comment.Id, CancellationToken.None);
        await service.ProcessUnreactCommentAsync(actor.Id, comment.Id, CancellationToken.None);

        var second = await service.ProcessUnreactCommentAsync(actor.Id, comment.Id, CancellationToken.None);

        Assert.True(second.IsFailed);
        Comment? stored = await FindCommentAsync(comment.Id);
        Assert.Equal(0, stored!.ReactionAmount.Value);
    }

    // ---------------------------------------------------------------- #49 GET /Reaction/post/{id}

    [Fact]
    public async Task GetReactionByPost_BlockedReactor_FilteredButPageStillFull()
    {
        Account viewer = await SeedAccountAsync();
        Account blockedReactor = await SeedAccountAsync();
        Account postAuthor = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(postAuthor.Id, shop.Id);
        await BlockAsync(viewer, blockedReactor);

        using var scope = _fixture.Services.CreateScope();
        var reactPostService = scope.ServiceProvider.GetRequiredService<IReactPostService>();

        await reactPostService.ProcessReactPostAsync(blockedReactor.Id, post.Id, CancellationToken.None);

        var visibleIds = new List<Guid>();
        for(int i = 0; i < 20; i++)
        {
            Account reactor = await SeedAccountAsync();
            await reactPostService.ProcessReactPostAsync(reactor.Id, post.Id, CancellationToken.None);
            visibleIds.Add(reactor.Id);
        }

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Reaction/post/{post.Id}?pageSize=20");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("accountId").GetGuid()).ToList();
        Assert.Equal(20, ids.Count);
        Assert.All(visibleIds, id => Assert.Contains(id, ids));
    }

    // ---------------------------------------------------------------- #50 GET /Reaction/comment/{id}

    [Fact]
    public async Task GetReactionByComment_BlockedReactor_IsFiltered()
    {
        Account viewer = await SeedAccountAsync();
        Account blockedReactor = await SeedAccountAsync();
        Account visibleReactor = await SeedAccountAsync();
        Account postAuthor = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(postAuthor.Id, shop.Id);
        Comment comment = await SeedCommentAsync(postAuthor.Id, post.Id);
        await BlockAsync(viewer, blockedReactor);

        using var scope = _fixture.Services.CreateScope();
        var reactCommentService = scope.ServiceProvider.GetRequiredService<IReactCommentService>();
        await reactCommentService.ProcessReactCommentAsync(blockedReactor.Id, comment.Id, CancellationToken.None);
        await reactCommentService.ProcessReactCommentAsync(visibleReactor.Id, comment.Id, CancellationToken.None);

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Reaction/comment/{comment.Id}");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("accountId").GetGuid()).ToList();
        Assert.Contains(visibleReactor.Id, ids);
        Assert.DoesNotContain(blockedReactor.Id, ids);
    }
}
