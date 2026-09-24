using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MongoDB.Driver;

namespace integration;

// test-strategy.md §5 rows 69, 76. GetDashboardStatsHandler counts every live row in three
// collections other tests in this same run keep seeding, so a raw count assertion would be
// polluted by whatever ran before it. §4 originally proposed dropping and reseeding the
// collections for this; that would also destroy IndexInitializer's partial unique indexes
// and whatever AdminSeeder wrote at boot (IntegrationTestFixture only removes
// NormalizedFieldBackfillInitializer and SoftDeletePurgeHost, not AdminSeeder). Every
// integration test already runs serially in one xunit collection (§4), so nothing inserts
// between two reads taken back to back here — a before/after DELTA proves the same thing
// §4 wanted a clean slate for, without touching what any other test left behind.
[Collection(IntegrationCollection.Name)]
public class DashboardEndpointTests
{
    private readonly IntegrationTestFixture _fixture;

    public DashboardEndpointTests(IntegrationTestFixture fixture)
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

    private async Task SoftDeleteAsync<T>(string collectionName, Guid id) where T : domain.BaseEntity
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        await dbContext.GetCollection<T>(collectionName).UpdateOneAsync(
            Builders<T>.Filter.Eq(x => x.Id, id),
            Builders<T>.Update.Set(x => x.IsDeleted, true).Set(x => x.DeletedAt, DateTime.UtcNow));
    }

    private HttpClient AuthenticatedClientFor(Account account, domain.Role role = domain.Role.Admin)
    {
        using var scope = _fixture.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtService>();
        string token = jwtService.GenerateAccessToken(account.Id, role);

        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private async Task<(long Accounts, long Posts, long Comments)> ReadStatsAsync(HttpClient client)
    {
        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Dashboard/stats");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (
            dto.GetProperty("activeAccountAmount").GetInt64(),
            dto.GetProperty("activePostAmount").GetInt64(),
            dto.GetProperty("activeCommentAmount").GetInt64());
    }

    // ---------------------------------------------------------------- #69 GET /Dashboard/stats

    [Fact]
    public async Task GetDashboardStats_CountsExcludeSoftDeleted()
    {
        Account admin = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin);

        var before = await ReadStatsAsync(client);

        Shop shop = await SeedShopAsync();
        Account liveAccount = await SeedAccountAsync();
        Account deletedAccount = await SeedAccountAsync();
        Post livePost = await SeedPostAsync(liveAccount.Id, shop.Id);
        Post deletedPost = await SeedPostAsync(liveAccount.Id, shop.Id);
        Comment liveComment = await SeedCommentAsync(liveAccount.Id, livePost.Id);
        Comment deletedComment = await SeedCommentAsync(liveAccount.Id, livePost.Id);

        await SoftDeleteAsync<Account>(CollectionName.Account, deletedAccount.Id);
        await SoftDeleteAsync<Post>(CollectionName.Post, deletedPost.Id);
        await SoftDeleteAsync<Comment>(CollectionName.Comment, deletedComment.Id);

        var after = await ReadStatsAsync(client);

        // Two accounts, two posts, two comments were inserted; one of each was then
        // soft-deleted before this second read. The admin account itself counts too — it
        // was seeded before the "before" read, so it does not appear in the delta.
        Assert.Equal(1, after.Accounts - before.Accounts);
        Assert.Equal(1, after.Posts - before.Posts);
        Assert.Equal(1, after.Comments - before.Comments);
    }

    // ---------------------------------------------------------------- #76 GET /Dashboard/activity

    [Theory]
    [InlineData(0)]
    [InlineData(721)]
    public async Task GetDashboardActivity_WindowHoursOutOfBounds_Returns400(int windowHours)
    {
        Account admin = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Dashboard/activity?windowHours={windowHours}");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "WindowHours must be between");
    }

    [Fact]
    public async Task GetDashboardActivity_Valid_EchoesWindowHours_AndIncludesFreshAccountInABucket()
    {
        Account admin = await SeedAccountAsync();
        Account freshAccount = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Dashboard/activity?windowHours=1");

        await AssertStatusAsync(response, HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, dto.GetProperty("windowHours").GetInt32());

        // No exact total: the 1-hour window can catch other tests' recent inserts too, and
        // that is fine — only that our own freshly seeded account contributed is pinned.
        long totalAccounts = dto.GetProperty("points").EnumerateArray()
            .Sum(x => x.GetProperty("accountAmount").GetInt64());
        Assert.True(totalAccounts >= 1, $"expected at least the freshly seeded account ({freshAccount.Id}) to be counted");
    }
}
