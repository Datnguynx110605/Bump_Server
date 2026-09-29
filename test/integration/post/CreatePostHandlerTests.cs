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
    private int _placeCalls;

    public CreatePostHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;

        // Default: an unscripted test reaching Apple is a bug in the test, not a silent pass.
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
        string placeId,
        List<(Stream stream, string fileName)>? images = null) =>
        new(placeId, caption: null, rate: Amount.Create(3).Value!, images);

    private static (Stream stream, string fileName) FakeImage(string name = "a.png") =>
        (new MemoryStream(Encoding.UTF8.GetBytes("not a real image, R2 does not care")), name);

    private sealed record PlaceStub(
        string PlaceId,
        string Name,
        string Address,
        double Latitude = 21.03,
        double Longitude = 105.85);

    // Answers the place lookup for each identifier given and counts how often it was asked.
    // The token exchange is deliberately not counted: MapsTokenProvider caches its token
    // process-wide, so whether a given test triggers one depends on what ran before it.
    private void RespondToMaps(params PlaceStub[] places)
    {
        _fixture.HttpHandler.Respond = request =>
        {
            string path = request.RequestUri!.AbsolutePath;

            if(path.StartsWith("/v1/token"))
                return JsonResponse(HttpStatusCode.OK, """{"accessToken":"test-token","expiresInSeconds":1800}""");

            PlaceStub? stub = places.FirstOrDefault(p => path.StartsWith($"/v1/place/{p.PlaceId}"));
            if(stub is null)
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            Interlocked.Increment(ref _placeCalls);

            return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                name = stub.Name,
                coordinate = new { latitude = stub.Latitude, longitude = stub.Longitude },
                formattedAddressLines = new[] { stub.Address }
            }));
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

    private async Task<Shop?> FindShopAsync(Guid shopId)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        return await dbContext.GetCollection<Shop>(CollectionName.Shop)
            .Find(x => x.Id == shopId)
            .FirstOrDefaultAsync();
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

    // The request carries nothing about the shop but the identifier, so every stored field
    // here came back from Apple. A client can claim any name it likes; it cannot invent an
    // identifier Apple will return a place for.
    [Fact]
    public async Task VerifiedPlace_IsStoredExactlyAsAppleAnsweredIt()
    {
        string placeId = $"place-{Guid.NewGuid():N}";
        RespondToMaps(new PlaceStub(placeId, "Verified Cafe", "42 Real Address", 21.03, 105.85));

        var result = await SendAsync(Command(placeId), Guid.NewGuid());

        Assert.True(result.IsSuccess);

        Shop? shop = await FindShopAsync(result.Value.ShopId);

        Assert.NotNull(shop);
        Assert.Equal("Verified Cafe", shop!.Name);
        Assert.Equal("42 Real Address", shop.Address);
        Assert.Equal(placeId, shop.PlaceId);
        // Stored longitude-first, the order the 2dsphere index expects.
        Assert.Equal(new[] { 105.85, 21.03 }, shop.Location);
    }

    // The whole point of looking the shop up before resolving it. The identifier a request
    // carries is the one echoed back in the answer, never read out of Apple's body, so the
    // lookup never needed the resolve — and a second review of a shop somebody has already
    // posted about costs no round trip to Apple at all.
    [Fact]
    public async Task RepeatPostAboutAKnownShop_MakesNoSecondPlaceCall()
    {
        string placeId = $"place-{Guid.NewGuid():N}";
        RespondToMaps(new PlaceStub(placeId, $"Repeat Shop {Guid.NewGuid():N}", "1 Test Street"));

        var first = await SendAsync(Command(placeId), Guid.NewGuid());
        var second = await SendAsync(Command(placeId), Guid.NewGuid());

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.ShopId, second.Value.ShopId);
        Assert.Equal(1, _placeCalls);
    }

    // Two kiosks in one mall, two units at one street number: Apple formats them identically
    // and issues each its own identifier. There is deliberately no unique index on the name
    // and address any more, because one would reject the second of these as a duplicate.
    [Fact]
    public async Task TwoPlacesSharingANameAndAddress_CreateTwoShops()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string firstPlaceId = $"place-a-{suffix}";
        string secondPlaceId = $"place-b-{suffix}";
        string name = $"Twin Kiosk {suffix}";
        string address = $"1 Mall Road {suffix}";

        RespondToMaps(
            new PlaceStub(firstPlaceId, name, address),
            new PlaceStub(secondPlaceId, name, address));

        var first = await SendAsync(Command(firstPlaceId), Guid.NewGuid());
        var second = await SendAsync(Command(secondPlaceId), Guid.NewGuid());

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value.ShopId, second.Value.ShopId);
    }

    // No available lever forces the two inserts to actually collide — the handler's re-read
    // filter mirrors the partial index exactly, so there is no daylight to widen deliberately.
    // If Mongo happens to serialize the two writes this run, both branches still pass (one
    // insert, one no-op find), just without exercising the recovery path; the mutation check
    // in test-strategy.md §9 step 3 is what actually proves this test depends on the index,
    // not this assertion alone.
    [Fact]
    public async Task ConcurrentInserts_SameNewShop_RecoverFromTheDuplicateKeyRace()
    {
        string placeId = $"place-{Guid.NewGuid():N}";
        Guid accountId = Guid.NewGuid();

        RespondToMaps(new PlaceStub(placeId, $"Race Shop {Guid.NewGuid():N}", "Race Address"));

        var results = await Task.WhenAll(
            SendAsync(Command(placeId), accountId),
            SendAsync(Command(placeId), accountId));

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Equal(results[0].Value.ShopId, results[1].Value.ShopId);

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        long shopCount = await dbContext.GetCollection<Shop>(CollectionName.Shop)
            .CountDocumentsAsync(x => x.PlaceId == placeId);

        Assert.Equal(1, shopCount);
    }

    // Apple is an external boundary, which is why Shop.Create still range-checks a coordinate
    // no client can supply any more. A place that resolves but answers nonsense is the one way
    // left to fail between the upload and the post insert, and the uploads must not survive it.
    [Fact]
    public async Task ShopCreationFailure_AfterUpload_DiscardsTheUploadedImages()
    {
        string placeId = $"place-{Guid.NewGuid():N}";
        RespondToMaps(new PlaceStub(placeId, "Impossible Cafe", "1 Test Street", Latitude: 999));

        int before = await CountBucketObjectsAsync();

        var result = await SendAsync(
            Command(placeId, images: [FakeImage()]),
            Guid.NewGuid());

        Assert.True(result.IsFailed);
        Assert.Equal(before, await CountBucketObjectsAsync());
    }

    [Fact]
    public async Task SuccessfulCreate_CachesThePostAndBumpsListVersions()
    {
        Guid accountId = Guid.NewGuid();
        string placeId = $"place-{Guid.NewGuid():N}";

        RespondToMaps(new PlaceStub(placeId, $"Cache Shop {Guid.NewGuid():N}", "1 Test Street"));

        using var scope = _fixture.Services.CreateScope();
        var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();

        long postVersionBefore = await redisService.GetCounterAsync(
            CacheKey.ListVersion(CollectionName.Post, null),
            CancellationToken.None);

        var result = await SendAsync(Command(placeId), accountId);

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
