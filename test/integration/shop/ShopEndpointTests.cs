using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MongoDB.Driver;

namespace integration;

// test-strategy.md §5 rows 39-44 and 78.
[Collection(IntegrationCollection.Name)]
public class ShopEndpointTests
{
    private readonly IntegrationTestFixture _fixture;

    public ShopEndpointTests(IntegrationTestFixture fixture)
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

    private async Task<Shop> SeedShopAsync(
        string? name = null,
        string? address = null,
        double? latitude = null,
        double? longitude = null,
        string? placeId = null,
        int rate = 0)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        Shop shop = Shop.Create(
            name ?? $"Shop {Guid.NewGuid():N}",
            address ?? "1 Test Street",
            latitude,
            longitude,
            placeId).Value!;

        if(rate != 0)
            shop.ChangeRate(Amount.Create(rate).Value!);

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

    private async Task<Shop?> FindShopAsync(Guid id)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        return await dbContext.GetCollection<Shop>(CollectionName.Shop)
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

    // ---------------------------------------------------------------- #39 DELETE /Shop/{id}

    [Fact]
    public async Task DeleteShop_NonAdmin_Returns403()
    {
        Account user = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        using var client = AuthenticatedClientFor(user);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Shop/{shop.Id}");

        await AssertStatusAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteShop_NotFound_Returns404()
    {
        Account admin = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Shop/{Guid.NewGuid()}");

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Shop not found");
    }

    [Fact]
    public async Task DeleteShop_StillHasLivePosts_Returns409()
    {
        Account admin = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        await SeedPostAsync(author.Id, shop.Id);

        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);
        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Shop/{shop.Id}");

        await AssertStatusAsync(response, HttpStatusCode.Conflict, "Shop still has posts. Delete them before deleting the shop.");
    }

    [Fact]
    public async Task DeleteShop_NoPosts_SoftDeletes()
    {
        Account admin = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Shop/{shop.Id}");

        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        Shop? stored = await FindShopAsync(shop.Id);
        Assert.True(stored!.IsDeleted);
    }

    // Direct-collection test, not through the endpoint: DeleteShopHandler does not create
    // shops, so the only way to observe "excluded from the partial unique indexes
    // afterwards" is against the index itself. Both directions are asserted so the test
    // actually discriminates the claim rather than merely showing an insert succeeds.
    [Fact]
    public async Task DeleteShop_ThenSameNameAndAddress_NoLongerCollidesOnTheUniqueIndex()
    {
        string name = $"Cafe {Guid.NewGuid():N}";
        string address = "42 Shared Street";
        Shop original = await SeedShopAsync(name, address);

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var shopCollection = dbContext.GetCollection<Shop>(CollectionName.Shop);

        Shop collidingWhileLive = Shop.Create(name, address).Value!;
        var thrown = await Assert.ThrowsAsync<MongoWriteException>(
            () => shopCollection.InsertOneAsync(collidingWhileLive));
        Assert.Equal(ServerErrorCategory.DuplicateKey, thrown.WriteError.Category);

        Account admin = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);
        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Shop/{original.Id}");
        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        Shop collidingAfterDelete = Shop.Create(name, address).Value!;
        await shopCollection.InsertOneAsync(collidingAfterDelete);

        Shop? stored = await FindShopAsync(collidingAfterDelete.Id);
        Assert.NotNull(stored);
    }

    // ---------------------------------------------------------------- #40 GET /Shop/{id}

    [Fact]
    public async Task GetSpecificShop_NotFound_Returns404()
    {
        Account viewer = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Shop/{Guid.NewGuid()}");

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Shop not found");
    }

    [Fact]
    public async Task GetSpecificShop_NotSaved_IsSavedFalse()
    {
        Account viewer = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Shop/{shop.Id}");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(dto.GetProperty("isSaved").GetBoolean());
    }

    [Fact]
    public async Task GetSpecificShop_Saved_IsSavedTrue()
    {
        Account viewer = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        await dbContext.GetCollection<Account>(CollectionName.Account).UpdateOneAsync(
            x => x.Id == viewer.Id,
            Builders<Account>.Update.Push(x => x.SaveShops, new SavedShop(shop.Id)));

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Shop/{shop.Id}");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(dto.GetProperty("isSaved").GetBoolean());
    }

    [Fact]
    public async Task GetSpecificShop_SecondCall_IsServedFromCache_NotReReadFromMongo()
    {
        Account viewer = await SeedAccountAsync();
        Shop shop = await SeedShopAsync(name: "Original Name");
        using var client = AuthenticatedClientFor(viewer);

        var first = await SendAsync(client, HttpMethod.Get, $"api/v1/Shop/{shop.Id}");
        await AssertStatusAsync(first, HttpStatusCode.OK);

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        await dbContext.GetCollection<Shop>(CollectionName.Shop).UpdateOneAsync(
            x => x.Id == shop.Id,
            Builders<Shop>.Update.Set(x => x.Name, "Mutated Directly In Mongo"));

        var second = await SendAsync(client, HttpMethod.Get, $"api/v1/Shop/{shop.Id}");
        await AssertStatusAsync(second, HttpStatusCode.OK);

        var dto = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Original Name", dto.GetProperty("shop").GetProperty("name").GetString());
    }

    // ---------------------------------------------------------------- #41 GET /Shop

    [Fact]
    public async Task GetAllShop_ExcludesSoftDeleted()
    {
        Account viewer = await SeedAccountAsync();
        Shop deleted = await SeedShopAsync();

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        deleted.DeleteShop();
        await dbContext.GetCollection<Shop>(CollectionName.Shop).UpdateOneAsync(
            x => x.Id == deleted.Id,
            Builders<Shop>.Update.Set(x => x.IsDeleted, true));

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Shop");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.DoesNotContain(deleted.Id, ids);
    }

    // ---------------------------------------------------------------- #42 GET /Shop/search

    [Fact]
    public async Task GetShopByKeyword_NormalizedMatch_IgnoresCaseAndDiacritics()
    {
        Account viewer = await SeedAccountAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        Shop shop = await SeedShopAsync(name: $"Cà Phê {suffix}");
        using var client = AuthenticatedClientFor(viewer);

        // Sent with diacritics and mixed case, the opposite of the stored NameNormalized
        // field, so a hit proves the incoming keyword itself gets normalized
        // (TextNormalizer.RemoveDiacriticsAndLower(request.keyword)) rather than merely
        // that the stored field already was.
        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Shop/search?keyword={Uri.EscapeDataString($"cÀ pHÊ {suffix}")}");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(shop.Id, ids);
    }

    // ---------------------------------------------------------------- #43 GET /Shop/by-name

    [Fact]
    public async Task GetShopByName_ExactMatch_Finds()
    {
        Account viewer = await SeedAccountAsync();
        string name = $"Exact Name {Guid.NewGuid():N}";
        Shop shop = await SeedShopAsync(name: name);
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Shop/by-name?name={Uri.EscapeDataString(name)}");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(shop.Id, ids);
    }

    // Pins the doc correction directly: /by-name filters the raw Name field, not
    // NameNormalized, so a caller relying on the same tolerance /search gives gets nothing.
    [Fact]
    public async Task GetShopByName_DifferentCase_DoesNotMatch()
    {
        Account viewer = await SeedAccountAsync();
        string name = $"CaseSensitive{Guid.NewGuid():N}";
        await SeedShopAsync(name: name);
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Shop/by-name?name={Uri.EscapeDataString(name.ToLowerInvariant())}");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(page.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task GetShopByName_MissingQueryParam_Returns400()
    {
        Account viewer = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Shop/by-name");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- #44 GET /Shop/by-address

    [Fact]
    public async Task GetShopByAddress_ExactMatch_Finds()
    {
        Account viewer = await SeedAccountAsync();
        string address = $"Exact Address {Guid.NewGuid():N}";
        Shop shop = await SeedShopAsync(address: address);
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Shop/by-address?address={Uri.EscapeDataString(address)}");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(shop.Id, ids);
    }

    [Fact]
    public async Task GetShopByAddress_DifferentCase_DoesNotMatch()
    {
        Account viewer = await SeedAccountAsync();
        string address = $"CaseSensitiveAddr{Guid.NewGuid():N}";
        await SeedShopAsync(address: address);
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Shop/by-address?address={Uri.EscapeDataString(address.ToLowerInvariant())}");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(page.GetProperty("items").EnumerateArray());
    }

    // ---------------------------------------------------------------- #78 GET /Shop/around

    // GetBestShopAround has no per-test filter signature to scope a query by — it is a raw
    // $nearSphere over the whole collection — so a fixed coordinate would pick up shops
    // every other test in this shared-container run ever seeded nearby. Each test instead
    // gets its own random origin, chosen far enough apart (mid-latitude band, full
    // longitude range) that another test's 5 km search radius essentially never overlaps it.
    private static (double Latitude, double Longitude) RandomOrigin() => (
        Random.Shared.NextDouble() * 120 - 60,
        Random.Shared.NextDouble() * 360 - 180);

    private static string AroundUrl(double latitude, double longitude) =>
        $"api/v1/Shop/around?latitude={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}&longitude={longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    [Fact]
    public async Task GetBestShopAround_LatitudeOutOfRange_Returns400()
    {
        Account viewer = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Shop/around?latitude=91&longitude=0");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Latitude must be between -90 and 90");
    }

    [Fact]
    public async Task GetBestShopAround_LongitudeOutOfRange_Returns400()
    {
        Account viewer = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Shop/around?latitude=0&longitude=181");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Longitude must be between -180 and 180");
    }

    [Fact]
    public async Task GetBestShopAround_UnverifiedShop_NoPlaceId_IsAbsent()
    {
        var (latitude, longitude) = RandomOrigin();
        Account viewer = await SeedAccountAsync();
        await SeedShopAsync(latitude: latitude, longitude: longitude, placeId: null, rate: 5);
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, AroundUrl(latitude, longitude));
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(page.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task GetBestShopAround_RateBelowFour_IsAbsent()
    {
        var (latitude, longitude) = RandomOrigin();
        Account viewer = await SeedAccountAsync();
        await SeedShopAsync(latitude: latitude, longitude: longitude, placeId: $"place-{Guid.NewGuid():N}", rate: 3);
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, AroundUrl(latitude, longitude));
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(page.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task GetBestShopAround_NoLocation_IsAbsent()
    {
        var (latitude, longitude) = RandomOrigin();
        Account viewer = await SeedAccountAsync();
        // A positive control at the same origin: without it, an empty result here would be
        // indistinguishable from "nothing was within 5 km of this random point" rather than
        // proof that the 2dsphere index actually skips a null Location.
        Shop located = await SeedShopAsync(latitude: latitude, longitude: longitude, placeId: $"place-{Guid.NewGuid():N}", rate: 5);
        await SeedShopAsync(placeId: $"place-{Guid.NewGuid():N}", rate: 5);
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, AroundUrl(latitude, longitude));
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([located.Id], ids);
    }

    [Fact]
    public async Task GetBestShopAround_BeyondFiveKilometers_IsAbsent()
    {
        var (latitude, longitude) = RandomOrigin();
        Account viewer = await SeedAccountAsync();
        // ~0.1 degree of latitude is roughly 11 km, well past the 5000m MaxDistanceMeters.
        await SeedShopAsync(latitude: latitude + 0.1, longitude: longitude, placeId: $"place-{Guid.NewGuid():N}", rate: 5);
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, AroundUrl(latitude, longitude));
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(page.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task GetBestShopAround_MultipleCandidates_OrderedByRateDescending()
    {
        var (latitude, longitude) = RandomOrigin();
        Account viewer = await SeedAccountAsync();
        Shop lowerRated = await SeedShopAsync(latitude: latitude, longitude: longitude, placeId: $"place-{Guid.NewGuid():N}", rate: 4);
        Shop higherRated = await SeedShopAsync(latitude: latitude + 0.0005, longitude: longitude, placeId: $"place-{Guid.NewGuid():N}", rate: 5);
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, AroundUrl(latitude, longitude));
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([higherRated.Id, lowerRated.Id], ids);
    }

    [Fact]
    public async Task GetBestShopAround_HasNoNextPage()
    {
        var (latitude, longitude) = RandomOrigin();
        Account viewer = await SeedAccountAsync();
        await SeedShopAsync(latitude: latitude, longitude: longitude, placeId: $"place-{Guid.NewGuid():N}", rate: 5);
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, AroundUrl(latitude, longitude));
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(page.GetProperty("hasMore").GetBoolean());
        Assert.True(page.GetProperty("nextCursor").ValueKind is JsonValueKind.Null);
    }
}
