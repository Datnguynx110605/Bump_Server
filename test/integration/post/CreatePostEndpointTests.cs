using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace integration;

// test-strategy.md §5 row 24, the E tier only. CreatePostHandlerTests.cs already covers
// CreatePostHandler through IMediator.Send — the six named cases in §3.3 (place
// resolution, shop adoption, the duplicate-key race, R2 cleanup) — bypassing the
// controller entirely. That means ImageUpload.ToStreams (layer 2, the multipart path) and
// Amount.Create/CreatePostValidator's layer 2/3 split have never run through this
// endpoint until now.
[Collection(IntegrationCollection.Name)]
public class CreatePostEndpointTests
{
    private readonly IntegrationTestFixture _fixture;

    public CreatePostEndpointTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;

        // No test here sends a placeId, so nothing reaches Apple Maps — matches
        // CreatePostHandlerTests's own default.
        _fixture.HttpHandler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotImplemented);
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

    private HttpClient AuthenticatedClientFor(Account account)
    {
        using var scope = _fixture.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtService>();
        string token = jwtService.GenerateAccessToken(account.Id, domain.Role.User);

        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static readonly byte[] FakePng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private sealed record FormField(string Name, string Value);

    private static MultipartFormDataContent BuildForm(
        string? shopName = "Cafe",
        string? shopAddress = "1 Main St",
        string? placeId = null,
        double? latitude = null,
        double? longitude = null,
        string? caption = null,
        int rate = 3,
        IEnumerable<(byte[] Bytes, string FileName)>? images = null,
        bool includeRate = true)
    {
        var form = new MultipartFormDataContent();

        void AddIfNotNull(string name, string? value)
        {
            if(value is not null)
                form.Add(new StringContent(value), name);
        }

        AddIfNotNull("placeId", placeId);
        AddIfNotNull("shopName", shopName);
        AddIfNotNull("shopAddress", shopAddress);
        if(latitude is not null)
            form.Add(new StringContent(latitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)), "latitude");
        if(longitude is not null)
            form.Add(new StringContent(longitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)), "longitude");
        AddIfNotNull("caption", caption);
        if(includeRate)
            form.Add(new StringContent(rate.ToString()), "rate");

        foreach(var (bytes, fileName) in images ?? [])
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(content, "images", fileName);
        }

        return form;
    }

    // ---------------------------------------------------------------- field matrix

    [Fact]
    public async Task Valid_Minimal_Returns200()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await client.PostAsync("api/v1/Post", BuildForm());

        await AssertStatusAsync(response, HttpStatusCode.OK);
    }

    [Fact]
    public async Task NegativeRate_Returns400_FromAmountCreate_Layer2()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await client.PostAsync("api/v1/Post", BuildForm(rate: -1));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Amount must be a non-negative integer");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task RateOutOfOneToFive_Returns400_FromCreatePostValidator_Layer3(int rate)
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await client.PostAsync("api/v1/Post", BuildForm(rate: rate));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Rate must be between 1 and 5");
    }

    [Fact]
    public async Task NoPlaceId_MissingShopName_Returns400()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await client.PostAsync("api/v1/Post", BuildForm(shopName: null));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "ShopName is required");
    }

    [Fact]
    public async Task NoPlaceId_MissingShopAddress_Returns400()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await client.PostAsync("api/v1/Post", BuildForm(shopAddress: null));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "ShopAddress is required");
    }

    // §3.6.2's own worked example: two independent validator failures at once produce a
    // ValidationProblemDetails, a different response shape than any single-field failure.
    [Fact]
    public async Task NoPlaceId_NeitherShopNameNorAddress_ReturnsValidationProblemDetails()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await client.PostAsync("api/v1/Post", BuildForm(shopName: null, shopAddress: null));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"errors\"", body);
        Assert.Contains("ShopName is required", body);
        Assert.Contains("ShopAddress is required", body);
    }

    [Fact]
    public async Task LatitudeWithoutLongitude_Returns400()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await client.PostAsync("api/v1/Post", BuildForm(latitude: 10));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Latitude and longitude must be provided together");
    }

    [Fact]
    public async Task LatitudeOutOfRange_Returns400()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await client.PostAsync("api/v1/Post", BuildForm(latitude: 91, longitude: 0));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Latitude must be between -90 and 90");
    }

    [Fact]
    public async Task CaptionOverTwoThousandChars_Returns400()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await client.PostAsync("api/v1/Post", BuildForm(caption: new string('a', 2001)));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Caption is too long, maximum length is 2000 characters");
    }

    [Fact]
    public async Task PlaceIdOverTwoHundredChars_Returns400()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await client.PostAsync("api/v1/Post", BuildForm(placeId: new string('a', 201), shopName: null, shopAddress: null));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "PlaceId is too long, maximum length is 200 characters");
    }

    // Unlike ChangeAvatar (a single IFormFile parameter, which ASP.NET requires
    // multipart/form-data for and answers 415 when it isn't), CreatePost's [FromForm]
    // parameters are all individually-optional scalars. A JSON body doesn't trip a
    // media-type rejection here — every field just binds to its default (shopName/
    // shopAddress null, rate 0) and the request fails at validation instead, as three
    // independent errors. §3.6.2's "wrong content type → 415" row does not hold uniformly
    // across every [FromForm] endpoint; it depends on whether an IFormFile is required.
    [Fact]
    public async Task WrongContentType_Json_FailsValidation_NotUnsupportedMediaType()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/Post")
        {
            Content = JsonContent.Create(new { shopName = "Cafe", shopAddress = "1 Main St", rate = 3 })
        };
        var response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"errors\"", body);
        Assert.Contains("ShopName is required", body);
    }

    // ---------------------------------------------------------------- upload matrix

    [Fact]
    public async Task Upload_EmptyFile_Returns400()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await client.PostAsync("api/v1/Post", BuildForm(images: [([], "a.png")]));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "File is empty");
    }

    [Fact]
    public async Task Upload_OverFiveMegabytes_Returns400()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        byte[] tooLarge = new byte[5 * 1024 * 1024 + 1];
        FakePng.CopyTo(tooLarge, 0);

        var response = await client.PostAsync("api/v1/Post", BuildForm(images: [(tooLarge, "a.png")]));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "File is too large");
    }

    [Fact]
    public async Task Upload_ElleventhFile_Returns400_MaxIsTen()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var elevenFiles = Enumerable.Range(0, 11).Select(i => (FakePng, $"a{i}.png"));

        var response = await client.PostAsync("api/v1/Post", BuildForm(images: elevenFiles));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Too many files, maximum is 10");
    }

    [Fact]
    public async Task Upload_DisallowedExtension_Returns400()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await client.PostAsync("api/v1/Post", BuildForm(images: [(FakePng, "a.gif")]));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "File type is not supported");
    }

    [Fact]
    public async Task Upload_HtmlRenamedJpg_Returns400_ContentDoesNotMatch()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        byte[] html = Encoding.UTF8.GetBytes("<html><body>not an image</body></html>");

        var response = await client.PostAsync("api/v1/Post", BuildForm(images: [(html, "a.jpg")]));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "File content does not match a supported image format");
    }

    [Fact]
    public async Task Upload_ValidImage_Returns200_AndCreatesTheR2Object()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await client.PostAsync("api/v1/Post", BuildForm(images: [(FakePng, "a.png")]));
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        var images = dto.GetProperty("images").EnumerateArray().ToList();
        Assert.Single(images);
    }
}
