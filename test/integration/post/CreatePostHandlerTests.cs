using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using MediatR;
using MongoDB.Driver;

namespace integration;

[Collection(IntegrationCollection.Name)]
public class CreatePostHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public CreatePostHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;

        // Default: nothing configured needs to reach Apple, so a test that never touches
        // placeId never has to think about the stub.
        _fixture.HttpHandler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotImplemented);
    }

    private async Task<FluentResults.Result<PostDto>> SendAsync(CreatePostCommand command, Guid accountId)
    {
        TestIdentity.Current = accountId;
        using var scope = _fixture.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        return await mediator.Send(command);
    }

    private static CreatePostCommand Command(
        string? placeId = null,
        string? shopName = "Cafe " + nameof(CreatePostHandlerTests),
        string? shopAddress = "1 Test Street",
        double? latitude = null,
        double? longitude = null,
        List<(Stream stream, string fileName)>? images = null)
    {
        return new CreatePostCommand(
            placeId,
            placeId is null ? shopName : null,
            placeId is null ? shopAddress : null,
            latitude,
            longitude,
            caption: null,
            rate: Amount.Create(3).Value!,
            images);
    }

    private static (Stream stream, string fileName) FakeImage(string name = "a.png") =>
        (new MemoryStream(Encoding.UTF8.GetBytes("not a real image, R2 does not care")), name);

    private void RespondToMaps(string placeId, string name, string address, double latitude, double longitude)
    {
        _fixture.HttpHandler.Respond = request =>
        {
            string path = request.RequestUri!.AbsolutePath;

            if(path.StartsWith("/v1/token"))
                return JsonResponse(HttpStatusCode.OK, """{"accessToken":"test-token","expiresInSeconds":1800}""");

            if(path.StartsWith($"/v1/place/{placeId}"))
                return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    name,
                    coordinate = new { latitude, longitude },
                    formattedAddressLines = new[] { address }
                }));

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
    }

    private void RespondPlaceNotFound()
    {
        _fixture.HttpHandler.Respond = request =>
        {
            string path = request.RequestUri!.AbsolutePath;

            return path.StartsWith("/v1/token")
                ? JsonResponse(HttpStatusCode.OK, """{"accessToken":"test-token","expiresInSeconds":1800}""")
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        };
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private async Task<int> CountBucketObjectsAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var s3 = scope.ServiceProvider.GetRequiredService<IAmazonS3>();

        var response = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = "bump-test" });

        return response.S3Objects?.Count ?? 0;
    }

    [Fact]
    public async Task PlaceCouldNotBeVerified_ReturnsBadRequest_BeforeAnyUpload()
    {
        RespondPlaceNotFound();
        int before = await CountBucketObjectsAsync();

        var result = await SendAsync(
            Command(placeId: "unknown-place", images: [FakeImage()]),
            Guid.NewGuid());

        Assert.True(result.IsFailed);
        Assert.Contains(result.Errors, e => e.Message == "Shop could not be verified");
        Assert.Equal(before, await CountBucketObjectsAsync());
    }

    [Fact]
    public async Task VerifiedPlace_OverridesClientSuppliedShopFields()
    {
        string placeId = $"place-{Guid.NewGuid():N}";
        RespondToMaps(placeId, "Verified Cafe", "42 Real Address", 21.03, 105.85);

        var command = new CreatePostCommand(
            placeId,
            shopName: "Client Made This Up",
            shopAddress: "Nowhere",
            latitude: 0,
            longitude: 0,
            caption: null,
            rate: Amount.Create(4).Value!,
            images: null);

        var result = await SendAsync(command, Guid.NewGuid());

        Assert.True(result.IsSuccess);

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var shop = await dbContext.GetCollection<Shop>(CollectionName.Shop)
            .Find(x => x.Id == result.Value.ShopId)
            .FirstOrDefaultAsync();

        Assert.NotNull(shop);
        Assert.Equal("Verified Cafe", shop!.Name);
        Assert.Equal("42 Real Address", shop.Address);
        Assert.Equal(placeId, shop.PlaceId);
        Assert.Equal(new[] { 105.85, 21.03 }, shop.Location);
    }

    [Fact]
    public async Task ExistingShopMatchedByPlaceId_WinsOverOneMatchedByNameAddress()
    {
        string placeId = $"place-{Guid.NewGuid():N}";
        string sharedName = $"Shared Name {Guid.NewGuid():N}";
        string sharedAddress = "Shared Address";

        using(var scope = _fixture.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
            var shopCollection = dbContext.GetCollection<Shop>(CollectionName.Shop);

            // The row a nameAddressFilter match would find — same name/address the place
            // resolves to, but no PlaceId of its own.
            Shop nameMatch = Shop.Create(sharedName, sharedAddress).Value!;
            await shopCollection.InsertOneAsync(nameMatch);

            // The row that already carries the identifier, under different text entirely.
            Shop placeMatch = Shop.Create("Different Name", "Different Address", placeId: placeId).Value!;
            await shopCollection.InsertOneAsync(placeMatch);

            RespondToMaps(placeId, sharedName, sharedAddress, 1, 2);

            var result = await SendAsync(
                new CreatePostCommand(placeId, null, null, null, null, null, Amount.Create(3).Value!, null),
                Guid.NewGuid());

            Assert.True(result.IsSuccess);
            Assert.Equal(placeMatch.Id, result.Value.ShopId);
        }
    }

    [Fact]
    public async Task UnverifiedShop_SecondPostWithSameNameAddress_ReusesTheFirstShop()
    {
        string name = $"Reused Shop {Guid.NewGuid():N}";
        string address = "Same Address Both Times";
        Guid accountId = Guid.NewGuid();

        var first = await SendAsync(Command(shopName: name, shopAddress: address), accountId);
        var second = await SendAsync(Command(shopName: name, shopAddress: address), accountId);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.ShopId, second.Value.ShopId);
    }

    [Fact]
    public async Task VerifiedPlace_AdoptsAnUnverifiedShopMatchedByNameAndAddress()
    {
        string placeId = $"place-{Guid.NewGuid():N}";
        string name = $"Adoptable Shop {Guid.NewGuid():N}";
        string address = "123 Adopt Street";

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var shopCollection = dbContext.GetCollection<Shop>(CollectionName.Shop);
        var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();

        Shop unverified = Shop.Create(name, address).Value!;
        await shopCollection.InsertOneAsync(unverified);

        RespondToMaps(placeId, name, address, 10, 20);

        var result = await SendAsync(
            new CreatePostCommand(placeId, null, null, null, null, null, Amount.Create(3).Value!, null),
            Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal(unverified.Id, result.Value.ShopId);

        Shop? updated = await shopCollection.Find(x => x.Id == unverified.Id).FirstOrDefaultAsync();
        Assert.Equal(placeId, updated!.PlaceId);

        // AdoptPlaceAsync removes the cached summary rather than leaving it to expire —
        // otherwise it would read as unverified for up to a week.
        ShopSummaryDto? cached = await redisService.GetAsync<ShopSummaryDto>(
            CacheKey.Shop(unverified.Id),
            CancellationToken.None);
        Assert.Null(cached);
    }

    // No available lever forces the two inserts to actually collide — the handler's
    // re-read filters mirror the partial indexes exactly, so there is no daylight to widen
    // deliberately. If Mongo happens to serialize the two writes this run, both branches
    // still pass (one insert, one no-op find), just without exercising the recovery path;
    // the mutation check in test-strategy.md §9 step 3 is what actually proves this test
    // depends on the index, not this assertion alone.
    [Fact]
    public async Task ConcurrentInserts_SameNewShop_RecoverFromTheDuplicateKeyRace()
    {
        string name = $"Race Shop {Guid.NewGuid():N}";
        string address = "Race Address";
        Guid accountId = Guid.NewGuid();

        var results = await Task.WhenAll(
            SendAsync(Command(shopName: name, shopAddress: address), accountId),
            SendAsync(Command(shopName: name, shopAddress: address), accountId));

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Equal(results[0].Value.ShopId, results[1].Value.ShopId);

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        long shopCount = await dbContext.GetCollection<Shop>(CollectionName.Shop)
            .CountDocumentsAsync(x => x.Name == name && x.Address == address);

        Assert.Equal(1, shopCount);
    }

    [Fact]
    public async Task ShopCreationFailure_AfterUpload_DiscardsTheUploadedImages()
    {
        int before = await CountBucketObjectsAsync();

        using var scope = _fixture.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<CreatePostHandler>(scope.ServiceProvider);

        TestIdentity.Current = Guid.NewGuid();

        // CreatePostValidator mirrors Shop.Create's own guards, so a command this invalid
        // never reaches the handler through the normal IMediator.Send pipeline — the
        // validator rejects it first, before any image is uploaded. Reaching the handler's
        // own post-upload cleanup path (the thing this test exists to pin) means calling
        // it directly, bypassing the validator on purpose.
        var command = new CreatePostCommand(
            placeId: null,
            shopName: string.Empty,
            shopAddress: "irrelevant",
            latitude: null,
            longitude: null,
            caption: null,
            rate: Amount.Create(3).Value!,
            images: [FakeImage()]);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailed);
        Assert.Equal(before, await CountBucketObjectsAsync());
    }

    [Fact]
    public async Task SuccessfulCreate_CachesThePostAndBumpsListVersions()
    {
        Guid accountId = Guid.NewGuid();
        string name = $"Cache Shop {Guid.NewGuid():N}";

        using var scope = _fixture.Services.CreateScope();
        var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();

        long postVersionBefore = await redisService.GetCounterAsync(
            CacheKey.ListVersion(CollectionName.Post, null),
            CancellationToken.None);

        var result = await SendAsync(Command(shopName: name), accountId);

        Assert.True(result.IsSuccess);

        PostSummaryDto? cachedPost = await redisService.GetAsync<PostSummaryDto>(
            CacheKey.Post(result.Value.Id),
            CancellationToken.None);
        Assert.NotNull(cachedPost);

        long postVersionAfter = await redisService.GetCounterAsync(
            CacheKey.ListVersion(CollectionName.Post, null),
            CancellationToken.None);
        Assert.True(postVersionAfter > postVersionBefore);

        long accountListVersion = await redisService.GetCounterAsync(
            CacheKey.ListVersion(CollectionName.Post, $"account:{accountId}"),
            CancellationToken.None);
        Assert.True(accountListVersion > 0);

        long shopListVersion = await redisService.GetCounterAsync(
            CacheKey.ListVersion(CollectionName.Post, $"shop:{result.Value.ShopId}"),
            CancellationToken.None);
        Assert.True(shopListVersion > 0);
    }
}
