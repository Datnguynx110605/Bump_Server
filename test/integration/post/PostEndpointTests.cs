using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MongoDB.Driver;

namespace integration;

// test-strategy.md §5 rows 25–32, 71 and 73. Row 24 (POST /Post) has its own file,
// CreatePostEndpointTests.cs — CreatePostHandlerTests.cs already covers CreatePostHandler
// through IMediator.Send (the H tier and the six named cases in §3.3), so this area's new
// ground is the HTTP-only surface: the multipart upload matrix and the layer distinctions
// §3.6.1 describes.
[Collection(IntegrationCollection.Name)]
public class PostEndpointTests
{
    private readonly IntegrationTestFixture _fixture;

    public PostEndpointTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record RawBody(string Json);

    private static HttpContent? BuildContent(object? body) => body switch
    {
        null => null,
        RawBody raw => new StringContent(raw.Json, Encoding.UTF8, "application/json"),
        _ => JsonContent.Create(body)
    };

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = BuildContent(body) };
        return await client.SendAsync(request);
    }

    private static async Task AssertStatusAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string? bodyFragment = null,
        string? caseDescription = null)
    {
        string body = await response.Content.ReadAsStringAsync();
        string prefix = caseDescription is null ? "" : $"[{caseDescription}] ";
        Assert.True(expected == response.StatusCode, $"{prefix}expected {expected}, got {response.StatusCode}: {body}");

        if(bodyFragment is not null)
            Assert.Contains(bodyFragment, body);
    }

    private async Task<Account> SeedAccountAsync(string? name = null)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var bcryptService = scope.ServiceProvider.GetRequiredService<IBcryptService>();

        Account account = Account.Create(
            Email.Create($"acct-{Guid.NewGuid():N}@test.invalid").Value!,
            Name.Create(name ?? $"acct{Guid.NewGuid():N}"[..20]).Value!,
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

    private async Task<Post> SeedPostAsync(Guid accountId, Guid shopId, string? caption = null, int rate = 3)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        Post post = Post.Create(accountId, shopId, caption, Amount.Create(rate).Value!).Value!;
        await dbContext.GetCollection<Post>(CollectionName.Post).InsertOneAsync(post);

        return post;
    }

    private async Task<Post?> FindPostAsync(Guid id)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        return await dbContext.GetCollection<Post>(CollectionName.Post)
            .Find(x => x.Id == id)
            .FirstOrDefaultAsync();
    }

    private HttpClient AuthenticatedClientFor(Account account, domain.Role role = domain.Role.User)
    {
        using var scope = _fixture.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtService>();
        string token = jwtService.GenerateAccessToken(account.Id, role);

        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    // ---------------------------------------------------------------- #25 PUT /Post/{id}/caption

    public static TheoryData<string, ChangeCaptionRequest?, HttpStatusCode, string?> ChangeCaptionRows() => new()
    {
        { "valid", new ChangeCaptionRequest("new caption"), HttpStatusCode.NoContent, null },
        { "missing body", null, HttpStatusCode.UnsupportedMediaType, null },
        { "caption over 2000 chars", new ChangeCaptionRequest(new string('a', 2001)), HttpStatusCode.BadRequest, "Caption is too long, maximum length is 2000 characters" },
        { "caption at 2000-char limit", new ChangeCaptionRequest(new string('a', 2000)), HttpStatusCode.NoContent, null }
    };

    [Theory]
    [MemberData(nameof(ChangeCaptionRows))]
    public async Task ChangeCaption_InputMatrix(string description, ChangeCaptionRequest? body, HttpStatusCode expected, string? fragment)
    {
        Account account = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(account.Id, shop.Id);
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Put, $"api/v1/Post/{post.Id}/caption", body);

        await AssertStatusAsync(response, expected, fragment, description);
    }

    [Fact]
    public async Task ChangeCaption_NotTheAuthor_Returns404_NeverForbidden()
    {
        Account owner = await SeedAccountAsync();
        Account stranger = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(owner.Id, shop.Id);
        using var client = AuthenticatedClientFor(stranger);

        var response = await SendAsync(client, HttpMethod.Put, $"api/v1/Post/{post.Id}/caption", new ChangeCaptionRequest("hijacked"));

        // Ownership is baked into ChangeCaptionHandler's query filter
        // (x.AccountId == identity.Value), not checked separately, so a non-owner's
        // postId simply never matches — 404, never 403.
        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Post not found");
    }

    [Fact]
    public async Task ChangeCaption_Valid_ReDerivesCaptionNormalized()
    {
        Account account = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(account.Id, shop.Id, caption: "old");
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Put, $"api/v1/Post/{post.Id}/caption", new ChangeCaptionRequest("Càphê Mới"));
        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        Post? stored = await FindPostAsync(post.Id);
        Assert.Equal("Càphê Mới", stored!.Caption);
        Assert.Equal("caphe moi", stored.CaptionNormalized);
    }

    // ---------------------------------------------------------------- #26 PUT /Post/{id}/rate

    // §3.6.1's own worked example: rate=-1 fails Amount.Create at the controller (layer 2)
    // with one message, rate=0 passes that and fails ChangeRateValidator's
    // InclusiveBetween(1,5) (layer 3) with a different one.
    public static TheoryData<string, ChangeRateRequest?, HttpStatusCode, string?> ChangeRateRows() => new()
    {
        { "valid", new ChangeRateRequest(4), HttpStatusCode.NoContent, null },
        { "missing body", null, HttpStatusCode.UnsupportedMediaType, null },
        { "rate negative (layer 2, Amount.Create)", new ChangeRateRequest(-1), HttpStatusCode.BadRequest, "Amount must be a non-negative integer" },
        { "rate zero (layer 3, ChangeRateValidator)", new ChangeRateRequest(0), HttpStatusCode.BadRequest, "Rate must be between 1 and 5" },
        { "rate six (layer 3, ChangeRateValidator)", new ChangeRateRequest(6), HttpStatusCode.BadRequest, "Rate must be between 1 and 5" }
    };

    [Theory]
    [MemberData(nameof(ChangeRateRows))]
    public async Task ChangeRate_InputMatrix(string description, ChangeRateRequest? body, HttpStatusCode expected, string? fragment)
    {
        Account account = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(account.Id, shop.Id);
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Put, $"api/v1/Post/{post.Id}/rate", body);

        await AssertStatusAsync(response, expected, fragment, description);
    }

    [Fact]
    public async Task ChangeRate_Valid_PublishesRecalculateShopRate()
    {
        Account account = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(account.Id, shop.Id, rate: 3);
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Put, $"api/v1/Post/{post.Id}/rate", new ChangeRateRequest(5));
        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        Post? stored = await FindPostAsync(post.Id);
        Assert.Equal(5, stored!.Rate.Value);

        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.RecalculateShopRate && m.Message is RecalculateShopRateMessage rm && rm.ShopId == shop.Id);
    }

    // ---------------------------------------------------------------- #27 DELETE /Post/{id}

    // test-strategy.md §3.5: the twelve queue consumers in the fixture are live and
    // RecordingRabbitMQService calls through, so a delete here really is picked up by
    // PostService asynchronously. Asserting the post's own soft delete and the publish is
    // the E tier's job; the cascade (comments removed, R2 objects deleted) is the worker's
    // own test, invoking PostService directly — asserting it here would race the consumer.
    [Fact]
    public async Task DeletePost_Valid_SoftDeletes_AndPublishesBothMessages()
    {
        Account account = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(account.Id, shop.Id);
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Post/{post.Id}");
        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        Post? stored = await FindPostAsync(post.Id);
        Assert.True(stored!.IsDeleted);

        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.DeletePost && m.Message is PostMessage pm && pm.PostId == post.Id);
        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.RecalculateShopRate && m.Message is RecalculateShopRateMessage rm && rm.ShopId == shop.Id);
    }

    [Fact]
    public async Task DeletePost_NotTheAuthor_Returns404()
    {
        Account owner = await SeedAccountAsync();
        Account stranger = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(owner.Id, shop.Id);
        using var client = AuthenticatedClientFor(stranger);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Post/{post.Id}");

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Post not found");
    }

    // ---------------------------------------------------------------- #28 GET /Post/{id}

    [Fact]
    public async Task GetSpecificPost_NotFound_Returns404()
    {
        Account viewer = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Post/{Guid.NewGuid()}");

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Post not found");
    }

    [Fact]
    public async Task GetSpecificPost_Valid_IsReactedAndIsSavedReflectTheViewer()
    {
        Account author = await SeedAccountAsync();
        Account viewer = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Post/{post.Id}");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(dto.GetProperty("isReacted").GetBoolean());
        Assert.False(dto.GetProperty("isSaved").GetBoolean());
        Assert.Equal(post.Id, dto.GetProperty("post").GetProperty("id").GetGuid());
    }

    // ---------------------------------------------------------------- #29 GET /Post/mine

    [Fact]
    public async Task GetMyPost_Valid_OnlyReturnsTheCallersOwnPosts()
    {
        Account viewer = await SeedAccountAsync();
        Account other = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post mine = await SeedPostAsync(viewer.Id, shop.Id);
        Post theirs = await SeedPostAsync(other.Id, shop.Id);
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Post/mine");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(mine.Id, ids);
        Assert.DoesNotContain(theirs.Id, ids);
    }

    // ---------------------------------------------------------------- #30 GET /Post/account/{id}

    [Fact]
    public async Task GetPostByAccount_BlockedAuthor_ReturnsEmptyPage_Not404()
    {
        Account viewer = await SeedAccountAsync();
        Account blocked = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        await SeedPostAsync(blocked.Id, shop.Id);

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var postAccountCollection = dbContext.GetCollection<Account>(CollectionName.Account);
        await Task.WhenAll(
            postAccountCollection.UpdateOneAsync(
                x => x.Id == viewer.Id,
                Builders<Account>.Update.Push(x => x.Blocks, new Block(blocked.Id, blocked.Name, null!))),
            postAccountCollection.UpdateOneAsync(
                x => x.Id == blocked.Id,
                Builders<Account>.Update.Push(x => x.BlockedBy, viewer.Id)));

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Post/account/{blocked.Id}");

        await AssertStatusAsync(response, HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(page.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task GetPostByAccount_Valid_ReturnsThatAccountsPosts()
    {
        Account viewer = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Post/account/{author.Id}");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(page.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == post.Id);
    }

    // ---------------------------------------------------------------- #31 GET /Post/shop/{id}

    [Fact]
    public async Task GetPostByShop_Valid_ReturnsPostsForThatShop()
    {
        Account viewer = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Shop otherShop = await SeedShopAsync();
        Post inShop = await SeedPostAsync(author.Id, shop.Id);
        Post otherShopPost = await SeedPostAsync(author.Id, otherShop.Id);
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Post/shop/{shop.Id}");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(inShop.Id, ids);
        Assert.DoesNotContain(otherShopPost.Id, ids);
    }

    // test-strategy.md §3.7's CachedCursorPaginator cache-key-split regression: the list
    // version counter is read under the unsuffixed filter signature and the page under the
    // one BlockedAccountSet.Signature suffixes with the viewer's hidden digest. A write by a
    // non-blocked author only knows the plain "shop:{id}" signature to invalidate — if the
    // counter were keyed by the suffixed signature instead, that bump could never reach a
    // blocking viewer's page key, and the new post would stay invisible behind the stale
    // cache entry until DefaultListTTL expired it.
    [Fact]
    public async Task GetPostByShop_NewPostFromNonBlockedAuthor_IsVisibleToAViewerWhoHasBlockedSomeoneElse()
    {
        Account viewer = await SeedAccountAsync();
        Account blockedAuthor = await SeedAccountAsync();
        Account otherAuthor = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        await SeedPostAsync(blockedAuthor.Id, shop.Id);

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        await dbContext.GetCollection<Account>(CollectionName.Account).UpdateOneAsync(
            x => x.Id == viewer.Id,
            Builders<Account>.Update.Push(x => x.Blocks, new Block(blockedAuthor.Id, blockedAuthor.Name, null!)));

        using var client = AuthenticatedClientFor(viewer);

        // Populates the cache under the viewer's hidden-suffixed page key.
        var first = await SendAsync(client, HttpMethod.Get, $"api/v1/Post/shop/{shop.Id}");
        await AssertStatusAsync(first, HttpStatusCode.OK);

        // CreatePostHandler invalidates (post, "shop:{shopId}") — the unsuffixed signature —
        // regardless of who ends up viewing the shop afterwards.
        TestIdentity.Current = otherAuthor.Id;
        var mediator = scope.ServiceProvider.GetRequiredService<MediatR.IMediator>();
        var created = await mediator.Send(new CreatePostCommand(
            shop.PlaceId, "new post", Amount.Create(4).Value!, null));
        Assert.True(created.IsSuccess);

        var second = await SendAsync(client, HttpMethod.Get, $"api/v1/Post/shop/{shop.Id}");
        await AssertStatusAsync(second, HttpStatusCode.OK);

        var secondPage = await second.Content.ReadFromJsonAsync<JsonElement>();
        var idsAfterWrite = secondPage.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid())
            .ToList();
        Assert.Contains(created.Value!.Id, idsAfterWrite);
    }

    // ---------------------------------------------------------------- #32 GET /Post/search

    [Fact]
    public async Task GetPostByKeyword_MissingQueryParam_Returns400()
    {
        Account viewer = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Post/search");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetPostByKeyword_NormalizedMatch_FindsCaptionSubstring()
    {
        Account viewer = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id, caption: "Best càphê in town");
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Post/search?keyword=caphe");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(page.GetProperty("items").EnumerateArray(), x => x.GetProperty("post").GetProperty("id").GetGuid() == post.Id);
    }

    [Fact]
    public async Task GetPostByKeyword_BlockedAuthor_FilteredButPageStillFull()
    {
        Account viewer = await SeedAccountAsync();
        Account blockedAuthor = await SeedAccountAsync();
        Account visibleAuthor = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var keywordAccountCollection = dbContext.GetCollection<Account>(CollectionName.Account);
        await Task.WhenAll(
            keywordAccountCollection.UpdateOneAsync(
                x => x.Id == viewer.Id,
                Builders<Account>.Update.Push(x => x.Blocks, new Block(blockedAuthor.Id, blockedAuthor.Name, null!))),
            keywordAccountCollection.UpdateOneAsync(
                x => x.Id == blockedAuthor.Id,
                Builders<Account>.Update.Push(x => x.BlockedBy, viewer.Id)));

        string sharedCaption = $"sharedkeyword{Guid.NewGuid():N}";
        for (int i = 0; i < 5; i++)
        {
            await SeedPostAsync(blockedAuthor.Id, shop.Id, caption: sharedCaption);
        }
        var visibleIds = new List<Guid>();
        for (int i = 0; i < 20; i++)
        {
            Post visiblePost = await SeedPostAsync(visibleAuthor.Id, shop.Id, caption: sharedCaption);
            visibleIds.Add(visiblePost.Id);
        }

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Post/search?keyword={sharedCaption}&pageSize=20");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("post").GetProperty("id").GetGuid()).ToList();
        Assert.Equal(20, ids.Count);
        Assert.All(visibleIds, id => Assert.Contains(id, ids));
    }

    // ---------------------------------------------------------------- #77 GET /Post/related

    [Fact]
    public async Task GetRelatedPost_NoFollowsOrAffinityOrTrusted_FallsBackToOwnPosts()
    {
        Account viewer = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post ownPost = await SeedPostAsync(viewer.Id, shop.Id);
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Post/related");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var matches = page.GetProperty("items").EnumerateArray()
            .Where(x => x.GetProperty("post").GetProperty("id").GetGuid() == ownPost.Id)
            .ToList();
        Assert.Single(matches);
    }

    [Fact]
    public async Task GetRelatedPost_FollowedAccountsPost_AppearsInTheFeed()
    {
        Account viewer = await SeedAccountAsync();
        Account followed = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post followedPost = await SeedPostAsync(followed.Id, shop.Id);

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        await dbContext.GetCollection<Account>(CollectionName.Account).UpdateOneAsync(
            x => x.Id == viewer.Id,
            Builders<Account>.Update.Push(x => x.Followings, new Following(followed.Id, followed.Name, null!)));

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Post/related");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(page.GetProperty("items").EnumerateArray(), x => x.GetProperty("post").GetProperty("id").GetGuid() == followedPost.Id);
    }

    // ---------------------------------------------------------------- #71 GET /Post

    [Fact]
    public async Task GetAllPost_ExcludesSoftDeleted()
    {
        Account admin = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post deleted = await SeedPostAsync(author.Id, shop.Id);

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        await dbContext.GetCollection<Post>(CollectionName.Post).UpdateOneAsync(
            x => x.Id == deleted.Id,
            Builders<Post>.Update.Set(x => x.IsDeleted, true).Set(x => x.DeletedAt, DateTime.UtcNow));

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Post");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(page.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == deleted.Id);
    }

    // ---------------------------------------------------------------- #73 DELETE /Post/admin/{id}

    [Fact]
    public async Task AdminDeletePost_NotFound_Returns404()
    {
        Account admin = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Post/admin/{Guid.NewGuid()}");

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Post not found");
    }

    [Fact]
    public async Task AdminDeletePost_Valid_DeletesAnyAuthorsPost_AndPublishesSameCascade()
    {
        Account admin = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Post/admin/{post.Id}");
        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        Post? stored = await FindPostAsync(post.Id);
        Assert.True(stored!.IsDeleted);

        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.DeletePost && m.Message is PostMessage pm && pm.PostId == post.Id);
    }
}
