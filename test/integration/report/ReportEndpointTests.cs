using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MongoDB.Driver;

namespace integration;

// test-strategy.md §5 rows 63-66. CreateReport's target-exists check and duplicate guard,
// and every input case FluentValidation's exactly-one-of-three rule creates, apply here.
// GetReportByTarget's ReportTarget query parameter is the clean §3.6.1 split: a name model
// binding can't parse fails at layer 1, a numeric value model binding CAN parse but IsInEnum
// rejects fails at layer 3 — two different response shapes for what looks like one bad input.
[Collection(IntegrationCollection.Name)]
public class ReportEndpointTests
{
    private readonly IntegrationTestFixture _fixture;

    public ReportEndpointTests(IntegrationTestFixture fixture)
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

    // Layer-1 (model-binding) rejections carry ASP.NET's own generated wording, not text
    // this codebase owns, so those rows pass bodyFragment: null — the status code and shape
    // are the contract worth pinning there.
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

    private async Task<Report> SeedReportAsync(Guid reporterId, Guid? postId, Guid? commentId, Guid? accountId)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        Report report = Report.Create(reporterId, postId, commentId, accountId, "reason").Value!;
        await dbContext.GetCollection<Report>(CollectionName.Report).InsertOneAsync(report);

        return report;
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

    // ---------------------------------------------------------------- #63 POST /Report

    public static TheoryData<string, object?, HttpStatusCode, string?> CreateReportRows()
    {
        Guid somePostId = Guid.NewGuid();

        return new()
        {
            { "missing body", null, HttpStatusCode.UnsupportedMediaType, null },
            { "empty json object", new RawBody("{}"), HttpStatusCode.BadRequest, null },
            { "malformed json", new RawBody("{"), HttpStatusCode.BadRequest, null },
            { "no target", new CreateReportRequest(null, null, null, "reason"), HttpStatusCode.BadRequest, "A report must name exactly one of PostId, CommentId or AccountId" },
            { "two targets", new CreateReportRequest(somePostId, Guid.NewGuid(), null, "reason"), HttpStatusCode.BadRequest, "A report must name exactly one of PostId, CommentId or AccountId" },
            { "empty reason", new CreateReportRequest(somePostId, null, null, ""), HttpStatusCode.BadRequest, "Reason is required" },
            { "reason too long", new CreateReportRequest(somePostId, null, null, new string('a', 2001)), HttpStatusCode.BadRequest, "Reason is too long, maximum length is 2000 characters" },
            { "empty postId", new CreateReportRequest(Guid.Empty, null, null, "reason"), HttpStatusCode.BadRequest, "PostId is invalid" },
            { "empty commentId", new CreateReportRequest(null, Guid.Empty, null, "reason"), HttpStatusCode.BadRequest, "CommentId is invalid" },
            { "empty accountId", new CreateReportRequest(null, null, Guid.Empty, "reason"), HttpStatusCode.BadRequest, "AccountId is invalid" }
        };
    }

    [Theory]
    [MemberData(nameof(CreateReportRows))]
    public async Task CreateReport_InputMatrix(string description, object? body, HttpStatusCode expected, string? fragment)
    {
        Account reporter = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(reporter);

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Report", body);

        await AssertStatusAsync(response, expected, fragment, description);
    }

    // Two independent validator failures in one request — the response shape itself is
    // what §3.6.2 flags as only observable here, not from status alone.
    [Fact]
    public async Task CreateReport_TwoFieldsInvalid_ReturnsValidationProblemDetails()
    {
        Account reporter = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(reporter);

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Report",
            new CreateReportRequest(null, null, null, ""));

        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"errors\"", body);
    }

    [Fact]
    public async Task CreateReport_PostTargetDoesNotExist_Returns404()
    {
        Account reporter = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(reporter);

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Report",
            new CreateReportRequest(Guid.NewGuid(), null, null, "reason"));

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Report target not found");
    }

    [Fact]
    public async Task CreateReport_SameReporterSameTargetTwice_Returns409()
    {
        Account reporter = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        using var client = AuthenticatedClientFor(reporter);

        var first = await SendAsync(client, HttpMethod.Post, "api/v1/Report",
            new CreateReportRequest(post.Id, null, null, "first reason"));
        await AssertStatusAsync(first, HttpStatusCode.OK);

        var second = await SendAsync(client, HttpMethod.Post, "api/v1/Report",
            new CreateReportRequest(post.Id, null, null, "second reason"));

        await AssertStatusAsync(second, HttpStatusCode.Conflict, "You have already reported this");
    }

    [Fact]
    public async Task CreateReport_Valid_Returns200WithDto()
    {
        Account reporter = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);
        using var client = AuthenticatedClientFor(reporter);

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Report",
            new CreateReportRequest(post.Id, null, null, "spam"));

        await AssertStatusAsync(response, HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(reporter.Id, dto.GetProperty("reporterId").GetGuid());
        Assert.Equal(post.Id, dto.GetProperty("postId").GetGuid());
        Assert.Equal("spam", dto.GetProperty("reason").GetString());
    }

    // A different account reporting the same target is a separate row: the duplicate guard
    // is scoped to (ReporterId, target), not to the target alone.
    [Fact]
    public async Task CreateReport_DifferentReportersSameTarget_BothSucceed()
    {
        Account first = await SeedAccountAsync();
        Account second = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        Post post = await SeedPostAsync(author.Id, shop.Id);

        using var firstClient = AuthenticatedClientFor(first);
        var firstResponse = await SendAsync(firstClient, HttpMethod.Post, "api/v1/Report",
            new CreateReportRequest(post.Id, null, null, "reason"));
        await AssertStatusAsync(firstResponse, HttpStatusCode.OK);

        using var secondClient = AuthenticatedClientFor(second);
        var secondResponse = await SendAsync(secondClient, HttpMethod.Post, "api/v1/Report",
            new CreateReportRequest(post.Id, null, null, "reason"));
        await AssertStatusAsync(secondResponse, HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------- #64 GET /Report

    [Fact]
    public async Task GetAllReport_Admin_ReturnsPaginatedReports()
    {
        Account admin = await SeedAccountAsync();
        Account reporter = await SeedAccountAsync();
        Account target = await SeedAccountAsync();
        Report report = await SeedReportAsync(reporter.Id, null, null, target.Id);

        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);
        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Report");

        await AssertStatusAsync(response, HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(report.Id, ids);
    }

    // ---------------------------------------------------------------- #65 GET /Report/{id}

    [Fact]
    public async Task GetSpecificReport_NotFound_Returns404()
    {
        Account admin = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Report/{Guid.NewGuid()}");

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Report not found");
    }

    [Fact]
    public async Task GetSpecificReport_Found_Returns200()
    {
        Account admin = await SeedAccountAsync();
        Account reporter = await SeedAccountAsync();
        Account target = await SeedAccountAsync();
        Report report = await SeedReportAsync(reporter.Id, null, null, target.Id);

        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);
        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Report/{report.Id}");

        await AssertStatusAsync(response, HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(report.Id, dto.GetProperty("id").GetGuid());
    }

    // ---------------------------------------------------------------- #66 GET /Report/target

    // §3.6.1 predicts two different rejection shapes for an invalid enum: an unparseable
    // name failing at layer 1, a parseable-but-undefined number reaching GetReportByTargetValidator's
    // IsInEnum() rule at layer 3. Empirically it does not split that way — found while
    // writing this test, not designed for. ASP.NET Core's own enum query binder validates
    // Enum.IsDefined before the controller action ever runs, so `target=99` fails model
    // binding exactly like `target=nonsense` does ("The value '99' is invalid."), both as
    // ASP.NET's own ValidationProblemDetails. IsInEnum() is validator dead code on this
    // endpoint — reachable only by calling GetReportByTargetValidator directly, which
    // test/unit/app/report/GetReportByTargetValidatorTests does. See doc/test-strategy.md §8.
    [Theory]
    [InlineData("nonsense")]
    [InlineData("99")]
    public async Task GetReportByTarget_InvalidTarget_FailsModelBinding_ReturnsValidationProblemDetails(string target)
    {
        Account admin = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Report/target?target={target}&targetId={Guid.NewGuid()}");

        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"errors\"", body);
    }

    [Fact]
    public async Task GetReportByTarget_MissingTargetId_Returns400()
    {
        Account admin = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Report/target?target=Post");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "TargetId is required");
    }

    [Fact]
    public async Task GetReportByTarget_Post_ReturnsOnlyReportsOnThatPost()
    {
        Account admin = await SeedAccountAsync();
        Account reporter = await SeedAccountAsync();
        Account otherAccount = await SeedAccountAsync();
        Guid postId = Guid.NewGuid();
        Report matching = await SeedReportAsync(reporter.Id, postId, null, null);
        await SeedReportAsync(reporter.Id, null, null, otherAccount.Id);

        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);
        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Report/target?target=Post&targetId={postId}");

        await AssertStatusAsync(response, HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([matching.Id], ids);
    }

    // Exercises the less obvious branch of TargetFilter — filtering by the account that
    // FILED the reports, not one that was reported.
    [Fact]
    public async Task GetReportByTarget_Reporter_ReturnsOnlyReportsFiledByThatAccount()
    {
        Account admin = await SeedAccountAsync();
        Account prolificReporter = await SeedAccountAsync();
        Account otherReporter = await SeedAccountAsync();
        Account target = await SeedAccountAsync();
        Report matching = await SeedReportAsync(prolificReporter.Id, null, null, target.Id);
        await SeedReportAsync(otherReporter.Id, null, null, target.Id);

        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);
        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Report/target?target=Reporter&targetId={prolificReporter.Id}");

        await AssertStatusAsync(response, HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([matching.Id], ids);
    }
}
