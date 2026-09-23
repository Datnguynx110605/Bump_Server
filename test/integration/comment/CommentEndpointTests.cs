using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MongoDB.Driver;

namespace integration;

// test-strategy.md §5 rows 33-38, 70 and 74.
[Collection(IntegrationCollection.Name)]
public class CommentEndpointTests
{
    private readonly IntegrationTestFixture _fixture;

    public CommentEndpointTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpContent? BuildContent(object? body) => body switch
    {
        null => null,
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

    private async Task<Comment> SeedCommentAsync(Guid accountId, Guid postId, string? content = "hello")
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        Comment comment = Comment.Create(accountId, postId, content).Value!;
        await dbContext.GetCollection<Comment>(CollectionName.Comment).InsertOneAsync(comment);

        return comment;
    }

    private async Task<Post?> FindPostAsync(Guid id)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        return await dbContext.GetCollection<Post>(CollectionName.Post)
            .Find(x => x.Id == id)
            .FirstOrDefaultAsync();
    }

    private async Task<Comment?> FindCommentAsync(Guid id)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        return await dbContext.GetCollection<Comment>(CollectionName.Comment)
            .Find(x => x.Id == id)
            .FirstOrDefaultAsync();
    }

    // Writes both arrays, like the real Block worker does (§5 row 79): BlockGuard.IsBlockedAsync
    // (used by the Reaction area's worker tests) reads only the actor's own document, so a
    // one-sided write would be silently insufficient there. This file's own tests only ever
    // block from the viewer's side, where BlockedAccountSet.ResolveAsync reads the viewer's
    // own Blocks and the one-sided write was already enough — the second write is for
    // consistency with the other files that share this helper's name and shape, not because
    // any assertion here needs it.
    private async Task BlockAsync(Guid blockerId, Account blocked)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var accountCollection = dbContext.GetCollection<Account>(CollectionName.Account);

        await Task.WhenAll(
            accountCollection.UpdateOneAsync(
                x => x.Id == blockerId,
                Builders<Account>.Update.Push(x => x.Blocks, new Block(blocked.Id, blocked.Name, null!))),
            accountCollection.UpdateOneAsync(
                x => x.Id == blocked.Id,
                Builders<Account>.Update.Push(x => x.BlockedBy, blockerId)));
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

    // ---------------------------------------------------------------- #33 POST /Comment

    private static readonly byte[] FakePng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private static MultipartFormDataContent BuildForm(
        Guid? postId,
        string? content = "nice",
        IEnumerable<(byte[] Bytes, string FileName)>? images = null)
    {
        var form = new MultipartFormDataContent();

        if(postId is not null)
            form.Add(new StringContent(postId.Value.ToString()), "postId");
        if(content is not null)
            form.Add(new StringContent(content), "content");

        foreach(var (bytes, fileName) in images ?? [])
        {
            var content2 = new ByteArrayContent(bytes);
            content2.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(content2, "images", fileName);
        }

        return form;
    }

    [Fact]
    public async Task CreateComment_MissingPostId_Returns400()
    {
        Account author = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(author);

        var response = await client.PostAsync("api/v1/Comment", BuildForm(postId: null));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "PostId is required");
    }

    [Fact]
    public async Task CreateComment_PostNotFound_Returns404()
    {
        Account author = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(author);

        var response = await client.PostAsync("api/v1/Comment", BuildForm(postId: Guid.NewGuid()));

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Post not found");
    }

    [Fact]
    public async Task CreateComment_ContentOverTwoThousandChars_Returns400()
    {
        Account author = await SeedAccountAsync();
        Account postAuthor = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(postAuthor.Id, shop.Id);
        using var client = AuthenticatedClientFor(author);

        var response = await client.PostAsync("api/v1/Comment", BuildForm(post.Id, content: new string('a', 2001)));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Content is too long, maximum length is 2000 characters");
    }

    [Fact]
    public async Task CreateComment_BlockedPostAuthor_Returns404_SameAsPostNotFound()
    {
        Account viewer = await SeedAccountAsync();
        Account blockedAuthor = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(blockedAuthor.Id, shop.Id);
        await BlockAsync(viewer.Id, blockedAuthor);

        using var client = AuthenticatedClientFor(viewer);
        var response = await client.PostAsync("api/v1/Comment", BuildForm(post.Id));

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Post not found");
    }

    [Fact]
    public async Task CreateComment_Upload_DisallowedExtension_Returns400()
    {
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        using var client = AuthenticatedClientFor(author);

        var response = await client.PostAsync("api/v1/Comment", BuildForm(post.Id, images: [(FakePng, "a.gif")]));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "File type is not supported");
    }

    [Fact]
    public async Task CreateComment_Upload_OverFiveMegabytes_Returns400()
    {
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        using var client = AuthenticatedClientFor(author);

        byte[] tooLarge = new byte[5 * 1024 * 1024 + 1];
        FakePng.CopyTo(tooLarge, 0);

        var response = await client.PostAsync("api/v1/Comment", BuildForm(post.Id, images: [(tooLarge, "a.png")]));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "File is too large");
    }

    [Fact]
    public async Task CreateComment_NoContentAndNoImages_Returns200()
    {
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        using var client = AuthenticatedClientFor(author);

        var response = await client.PostAsync("api/v1/Comment", BuildForm(post.Id, content: null));

        await AssertStatusAsync(response, HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateComment_Valid_IncrementsCommentAmount_AndPublishesCreateComment()
    {
        Account author = await SeedAccountAsync();
        Account postAuthor = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(postAuthor.Id, shop.Id);
        using var client = AuthenticatedClientFor(author);

        var response = await client.PostAsync("api/v1/Comment", BuildForm(post.Id, images: [(FakePng, "a.png")]));
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        Guid commentId = dto.GetProperty("id").GetGuid();
        Assert.Single(dto.GetProperty("images").EnumerateArray());

        Post? stored = await FindPostAsync(post.Id);
        Assert.Equal(1, stored!.CommentAmount.Value);

        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.CreateComment &&
            m.Message is CommentMessage cm && cm.CommentId == commentId);
    }

    // ---------------------------------------------------------------- #34 PUT /Comment/{id}

    [Fact]
    public async Task ChangeContent_MissingBody_Returns415()
    {
        Account author = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(author);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/v1/Comment/{Guid.NewGuid()}");
        var response = await client.SendAsync(request);

        await AssertStatusAsync(response, HttpStatusCode.UnsupportedMediaType);
    }

    // ChangeContentRequest.Content is a non-nullable string, so an empty JSON object never
    // reaches ChangeContentValidator: System.Text.Json's record-constructor binding rejects
    // it as a missing required member at layer 1, with a framework-owned message rather
    // than the validator's "Content is required" (which is unreachable through the HTTP
    // surface for this reason — ChangeContentValidatorTests exercises it directly instead).
    [Fact]
    public async Task ChangeContent_MissingContentField_Returns400_FromModelBinding_Layer1()
    {
        Account author = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(author);

        var response = await SendAsync(client, HttpMethod.Put, $"api/v1/Comment/{Guid.NewGuid()}", new { });

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "The Content field is required.");
    }

    [Fact]
    public async Task ChangeContent_ContentOverTwoThousandChars_Returns400()
    {
        Account author = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(author);

        var response = await SendAsync(client, HttpMethod.Put, $"api/v1/Comment/{Guid.NewGuid()}", new ChangeContentRequest(new string('a', 2001)));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Content is too long, maximum length is 2000 characters");
    }

    [Fact]
    public async Task ChangeContent_NotFound_Returns404()
    {
        Account author = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(author);

        var response = await SendAsync(client, HttpMethod.Put, $"api/v1/Comment/{Guid.NewGuid()}", new ChangeContentRequest("edited"));

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Comment not found");
    }

    [Fact]
    public async Task ChangeContent_NotTheAuthor_Returns404_NeverForbidden()
    {
        Account owner = await SeedAccountAsync();
        Account stranger = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(owner.Id, shop.Id);
        Comment comment = await SeedCommentAsync(owner.Id, post.Id);

        using var client = AuthenticatedClientFor(stranger);
        var response = await SendAsync(client, HttpMethod.Put, $"api/v1/Comment/{comment.Id}", new ChangeContentRequest("edited"));

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Comment not found");
    }

    [Fact]
    public async Task ChangeContent_Valid_UpdatesStoredContent()
    {
        Account owner = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(owner.Id, shop.Id);
        Comment comment = await SeedCommentAsync(owner.Id, post.Id, content: "original");

        using var client = AuthenticatedClientFor(owner);
        var response = await SendAsync(client, HttpMethod.Put, $"api/v1/Comment/{comment.Id}", new ChangeContentRequest("edited"));

        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        Comment? stored = await FindCommentAsync(comment.Id);
        Assert.Equal("edited", stored!.Content);
    }

    // ---------------------------------------------------------------- #35 DELETE /Comment/{id}

    [Fact]
    public async Task DeleteComment_NotFound_Returns404()
    {
        Account author = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(author);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Comment/{Guid.NewGuid()}");

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Comment not found");
    }

    [Fact]
    public async Task DeleteComment_NotTheAuthor_Returns404_NeverForbidden()
    {
        Account owner = await SeedAccountAsync();
        Account stranger = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(owner.Id, shop.Id);
        Comment comment = await SeedCommentAsync(owner.Id, post.Id);

        using var client = AuthenticatedClientFor(stranger);
        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Comment/{comment.Id}");

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Comment not found");
    }

    [Fact]
    public async Task DeleteComment_Valid_SoftDeletes_DecrementsCounter_AndPublishesDeleteComment()
    {
        Account owner = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(owner.Id, shop.Id);
        Comment comment = await SeedCommentAsync(owner.Id, post.Id);

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        await dbContext.GetCollection<Post>(CollectionName.Post).UpdateOneAsync(
            x => x.Id == post.Id,
            Builders<Post>.Update.Inc(x => x.CommentAmount.Value, 1));

        using var client = AuthenticatedClientFor(owner);
        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Comment/{comment.Id}");

        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        Comment? storedComment = await FindCommentAsync(comment.Id);
        Assert.True(storedComment!.IsDeleted);

        Post? storedPost = await FindPostAsync(post.Id);
        Assert.Equal(0, storedPost!.CommentAmount.Value);

        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.DeleteComment &&
            m.Message is CommentMessage cm && cm.CommentId == comment.Id);
    }

    // CommentAmount is already 0 (SeedPostAsync never increments it), so the handler's
    // Gt(CommentAmount.Value, 0) precondition matches nothing and parentPost comes back
    // null. That takes the `if(parentPost is not null)` branch DeleteCommentHandler's own
    // comment names: the account/shop list invalidations are skipped, but the soft delete
    // and the DeleteComment publish still have to happen.
    [Fact]
    public async Task DeleteComment_CounterAlreadyAtZero_StillSoftDeletes_AndPublishes()
    {
        Account owner = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(owner.Id, shop.Id);
        Comment comment = await SeedCommentAsync(owner.Id, post.Id);

        using var client = AuthenticatedClientFor(owner);
        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Comment/{comment.Id}");

        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        Comment? storedComment = await FindCommentAsync(comment.Id);
        Assert.True(storedComment!.IsDeleted);

        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.DeleteComment &&
            m.Message is CommentMessage cm && cm.CommentId == comment.Id);
    }

    // ---------------------------------------------------------------- #36 GET /Comment/{id}

    [Fact]
    public async Task GetSpecificComment_NotFound_Returns404()
    {
        Account viewer = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Comment/{Guid.NewGuid()}");

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Comment not found");
    }

    [Fact]
    public async Task GetSpecificComment_BlockedAuthor_Returns404()
    {
        Account viewer = await SeedAccountAsync();
        Account blockedAuthor = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(blockedAuthor.Id, shop.Id);
        Comment comment = await SeedCommentAsync(blockedAuthor.Id, post.Id);
        await BlockAsync(viewer.Id, blockedAuthor);

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Comment/{comment.Id}");

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Comment not found");
    }

    [Fact]
    public async Task GetSpecificComment_SecondCall_IsServedFromCache_NotReReadFromMongo()
    {
        Account viewer = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        Comment comment = await SeedCommentAsync(author.Id, post.Id, content: "first");

        using var client = AuthenticatedClientFor(viewer);
        var first = await SendAsync(client, HttpMethod.Get, $"api/v1/Comment/{comment.Id}");
        await AssertStatusAsync(first, HttpStatusCode.OK);

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        await dbContext.GetCollection<Comment>(CollectionName.Comment).UpdateOneAsync(
            x => x.Id == comment.Id,
            Builders<Comment>.Update.Set(x => x.Content, "mutated directly in mongo"));

        var second = await SendAsync(client, HttpMethod.Get, $"api/v1/Comment/{comment.Id}");
        await AssertStatusAsync(second, HttpStatusCode.OK);

        var dto = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("first", dto.GetProperty("content").GetString());
    }

    // ---------------------------------------------------------------- #37 GET /Comment/post/{postId}

    [Fact]
    public async Task GetCommentByPost_BlockedAuthor_FilteredButPageStillFull()
    {
        Account viewer = await SeedAccountAsync();
        Account blockedAuthor = await SeedAccountAsync();
        Account visibleAuthor = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(visibleAuthor.Id, shop.Id);
        await BlockAsync(viewer.Id, blockedAuthor);

        for(int i = 0; i < 5; i++)
            await SeedCommentAsync(blockedAuthor.Id, post.Id);

        var visibleIds = new List<Guid>();
        for(int i = 0; i < 20; i++)
        {
            Comment comment = await SeedCommentAsync(visibleAuthor.Id, post.Id);
            visibleIds.Add(comment.Id);
        }

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Comment/post/{post.Id}?pageSize=20");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(20, ids.Count);
        Assert.All(visibleIds, id => Assert.Contains(id, ids));
    }

    // ---------------------------------------------------------------- #38 GET /Comment/mine

    [Fact]
    public async Task GetMyCommentHistory_OnlyReturnsOwnComments()
    {
        Account viewer = await SeedAccountAsync();
        Account other = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(viewer.Id, shop.Id);
        Comment mine = await SeedCommentAsync(viewer.Id, post.Id);
        await SeedCommentAsync(other.Id, post.Id);

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Comment/mine");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([mine.Id], ids);
    }

    // ---------------------------------------------------------------- #70 GET /Comment (admin)

    [Fact]
    public async Task GetAllComment_NonAdmin_Returns403()
    {
        Account user = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(user);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Comment");

        await AssertStatusAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAllComment_Admin_ExcludesSoftDeleted()
    {
        Account admin = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        await SeedCommentAsync(author.Id, post.Id);

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        Comment deleted = Comment.Create(author.Id, post.Id, "deleted").Value!;
        deleted.DeleteComment();
        await dbContext.GetCollection<Comment>(CollectionName.Comment).InsertOneAsync(deleted);

        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);
        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Comment");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        // Asserting only the negative: GetAllCommentQuery has no filter signature, so this
        // reads the whole collection across every test in the shared-container run, and
        // which page a freshly-seeded id lands on is not something this test controls.
        // "Filtered out" is exactly what the row claims and it holds regardless of paging.
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.DoesNotContain(deleted.Id, ids);
    }

    // ---------------------------------------------------------------- #74 DELETE /Comment/admin/{id}

    [Fact]
    public async Task AdminDeleteComment_NonAdmin_Returns403()
    {
        Account user = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(user);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Comment/admin/{Guid.NewGuid()}");

        await AssertStatusAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminDeleteComment_NotFound_Returns404()
    {
        Account admin = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Comment/admin/{Guid.NewGuid()}");

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Comment not found");
    }

    [Fact]
    public async Task AdminDeleteComment_NotTheAuthor_StillSucceeds_NoOwnershipCheck()
    {
        Account admin = await SeedAccountAsync();
        Account owner = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(owner.Id, shop.Id);
        Comment comment = await SeedCommentAsync(owner.Id, post.Id);

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        await dbContext.GetCollection<Post>(CollectionName.Post).UpdateOneAsync(
            x => x.Id == post.Id,
            Builders<Post>.Update.Inc(x => x.CommentAmount.Value, 1));

        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);
        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Comment/admin/{comment.Id}");

        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        Comment? stored = await FindCommentAsync(comment.Id);
        Assert.True(stored!.IsDeleted);

        Post? storedPost = await FindPostAsync(post.Id);
        Assert.Equal(0, storedPost!.CommentAmount.Value);

        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.DeleteComment &&
            m.Message is CommentMessage cm && cm.CommentId == comment.Id);
    }
}
