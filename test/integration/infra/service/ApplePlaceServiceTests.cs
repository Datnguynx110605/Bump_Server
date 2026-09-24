using System.Net;
using System.Text;
using System.Text.Json;

namespace integration;

[Collection(IntegrationCollection.Name)]
public class ApplePlaceServiceTests
{
    private readonly IntegrationTestFixture _fixture;

    public ApplePlaceServiceTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage TokenExchangeOk() =>
        JsonResponse(HttpStatusCode.OK, """{"accessToken":"test-token","expiresInSeconds":1800}""");

    [Fact]
    public async Task ResolveAsync_SuccessResponse_ReturnsThePlace()
    {
        _fixture.HttpHandler.Respond = request =>
        {
            string path = request.RequestUri!.AbsolutePath;

            if(path.StartsWith("/v1/token"))
                return TokenExchangeOk();

            return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                name = "Test Cafe",
                coordinate = new { latitude = 10.5, longitude = 106.7 },
                formattedAddressLines = new[] { "1 Test Street", "District 1" }
            }));
        };

        using var scope = _fixture.Services.CreateScope();
        var placeService = scope.ServiceProvider.GetRequiredService<IPlaceService>();

        var result = await placeService.ResolveAsync("place-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("place-1", result!.PlaceId);
        Assert.Equal("Test Cafe", result.Name);
        Assert.Equal("1 Test Street, District 1", result.Address);
        Assert.Equal(10.5, result.Latitude);
        Assert.Equal(106.7, result.Longitude);
    }

    [Fact]
    public async Task ResolveAsync_UnknownPlaceId_ReturnsNull()
    {
        _fixture.HttpHandler.Respond = request => request.RequestUri!.AbsolutePath.StartsWith("/v1/token")
            ? TokenExchangeOk()
            : new HttpResponseMessage(HttpStatusCode.NotFound);

        using var scope = _fixture.Services.CreateScope();
        var placeService = scope.ServiceProvider.GetRequiredService<IPlaceService>();

        var result = await placeService.ResolveAsync("unknown-place", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_5xxResponse_ReturnsNullRatherThanThrowing()
    {
        _fixture.HttpHandler.Respond = request => request.RequestUri!.AbsolutePath.StartsWith("/v1/token")
            ? TokenExchangeOk()
            : new HttpResponseMessage(HttpStatusCode.InternalServerError);

        using var scope = _fixture.Services.CreateScope();
        var placeService = scope.ServiceProvider.GetRequiredService<IPlaceService>();

        var result = await placeService.ResolveAsync("place-1", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_NetworkFailure_ReturnsNullRatherThanThrowing()
    {
        _fixture.HttpHandler.Respond = request =>
        {
            if(request.RequestUri!.AbsolutePath.StartsWith("/v1/token"))
                return TokenExchangeOk();

            throw new HttpRequestException("simulated network failure");
        };

        using var scope = _fixture.Services.CreateScope();
        var placeService = scope.ServiceProvider.GetRequiredService<IPlaceService>();

        var result = await placeService.ResolveAsync("place-1", CancellationToken.None);

        Assert.Null(result);
    }

    // Not pinned in test-strategy.md's §3.4 row for this adapter, but read out of
    // ApplePlaceService.ReadPlace while writing these tests: nothing between
    // ReadAsStringAsync and the JsonSerializer.Deserialize call catches JsonException, so a
    // malformed 200 body currently throws out of ResolveAsync rather than degrading to null
    // like every other failure mode here does. Pinned as the row's "malformed body degrades"
    // claim does not hold for this adapter today — see the correction in test-strategy.md.
    [Fact]
    public async Task ResolveAsync_MalformedBody_ThrowsJsonException()
    {
        _fixture.HttpHandler.Respond = request => request.RequestUri!.AbsolutePath.StartsWith("/v1/token")
            ? TokenExchangeOk()
            : JsonResponse(HttpStatusCode.OK, "not valid json");

        using var scope = _fixture.Services.CreateScope();
        var placeService = scope.ServiceProvider.GetRequiredService<IPlaceService>();

        await Assert.ThrowsAsync<JsonException>(
            () => placeService.ResolveAsync("place-1", CancellationToken.None));
    }
}
