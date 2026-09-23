using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MongoDB.Driver;

namespace integration;

// test-strategy.md §5 rows 17–20 and 75. LoginHandler and RequestNewAccessTokenHandler are
// both in the document's already-read provenance list, so their pinned-behaviour cells are
// trustworthy starting points rather than hypotheses to re-derive from scratch.
[Collection(IntegrationCollection.Name)]
public class AuthEndpointTests
{
    private readonly IntegrationTestFixture _fixture;

    public AuthEndpointTests(IntegrationTestFixture fixture)
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

    private async Task<(Account Account, string Password)> SeedAccountAsync(
        string? email = null,
        string password = "correct-password",
        bool deleted = false)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var bcryptService = scope.ServiceProvider.GetRequiredService<IBcryptService>();

        Account account = Account.Create(
            Email.Create(email ?? $"acct-{Guid.NewGuid():N}@test.invalid").Value!,
            Name.Create($"acct{Guid.NewGuid():N}"[..20]).Value!,
            bcryptService.HashPassword(password),
            bio: null).Value!;

        if(deleted)
            account.Delete();

        await dbContext.GetCollection<Account>(CollectionName.Account).InsertOneAsync(account);

        return (account, password);
    }

    private async Task<RefreshToken> SeedRefreshTokenAsync(Guid accountId, bool revoked = false, DateTime? expiryDate = null)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var collection = dbContext.GetCollection<RefreshToken>(CollectionName.RefreshToken);

        RefreshToken token = RefreshToken.Create(accountId).Value!;
        await collection.InsertOneAsync(token);

        if(revoked || expiryDate is not null)
        {
            var update = Builders<RefreshToken>.Update.Combine(
                new List<UpdateDefinition<RefreshToken>>().Concat(revoked ? [Builders<RefreshToken>.Update.Set(x => x.IsRevoked, true)] : [])
                    .Concat(expiryDate is not null ? [Builders<RefreshToken>.Update.Set(x => x.ExpiryDate, expiryDate!.Value)] : []));

            await collection.UpdateOneAsync(x => x.Id == token.Id, update);
        }

        return token;
    }

    private async Task<RefreshToken?> FindRefreshTokenAsync(Guid id)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        return await dbContext.GetCollection<RefreshToken>(CollectionName.RefreshToken)
            .Find(x => x.Id == id)
            .FirstOrDefaultAsync();
    }

    private async Task<long> CountLiveRefreshTokensAsync(Guid accountId)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        return await dbContext.GetCollection<RefreshToken>(CollectionName.RefreshToken)
            .CountDocumentsAsync(x => x.AccountId == accountId && !x.IsRevoked);
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

    // ---------------------------------------------------------------- #17 POST /Auth/login

    // "valid" carries a null body and a true sentinel rather than a literal LoginRequest —
    // a real success needs an account seeded with a known password, which MemberData can't
    // do (it runs synchronously, before the theory body's async arrange). The theory below
    // seeds one and substitutes it whenever seedRealAccount is true.
    //
    // The 200/201-char rows pin the boundary itself, not just a message: both Email.Create
    // and LoginValidator's MaximumLength(200) answer "Email is invalid", so a row that only
    // checks that text can't tell which layer fired. At exactly 200 chars both checks pass
    // and the request reaches LoginHandler, which answers 401 "Invalid credentials" for an
    // email nothing is seeded under — a different status is what proves the boundary moved.
    public static TheoryData<string, object?, HttpStatusCode, string?, bool> LoginRows()
    {
        return new()
        {
            { "valid", null, HttpStatusCode.OK, null, true },
            { "missing body", null, HttpStatusCode.UnsupportedMediaType, null, false },
            { "malformed json", new RawBody("{"), HttpStatusCode.BadRequest, null, false },
            { "empty email", new LoginRequest("", "password"), HttpStatusCode.BadRequest, "Email is required", false },
            { "empty password", new LoginRequest("someone@test.invalid", ""), HttpStatusCode.BadRequest, "Password is required", false },
            { "malformed email (no @)", new LoginRequest("not-an-email", "password"), HttpStatusCode.BadRequest, "Email is invalid", false },
            { "email at 200-char limit (unseeded)", new LoginRequest($"{new string('a', 191)}@test.com", "password"), HttpStatusCode.Unauthorized, "Invalid credentials", false },
            { "email over 200 chars", new LoginRequest($"{new string('a', 192)}@test.com", "password"), HttpStatusCode.BadRequest, "Email is invalid", false }
        };
    }

    [Theory]
    [MemberData(nameof(LoginRows))]
    public async Task Login_InputMatrix(string description, object? body, HttpStatusCode expected, string? fragment, bool seedRealAccount)
    {
        using var client = _fixture.CreateAnonymousClient();

        if(seedRealAccount)
        {
            var (account, password) = await SeedAccountAsync();
            body = new LoginRequest(account.Email.Value, password);
        }

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Auth/login", body);

        await AssertStatusAsync(response, expected, fragment, description);
    }

    // The three refusal paths must be indistinguishable, or the endpoint enumerates which
    // accounts exist. Comparing the exact response bodies, not just the status, is the
    // point — three different messages at 401 would still fail the property this pins.
    [Fact]
    public async Task Login_UnknownEmail_SoftDeletedAccount_AndWrongPassword_AreByteIdentical()
    {
        using var client = _fixture.CreateAnonymousClient();
        var (deletedAccount, deletedPassword) = await SeedAccountAsync(deleted: true);
        var (liveAccount, _) = await SeedAccountAsync();

        var unknownResponse = await SendAsync(client, HttpMethod.Post, "api/v1/Auth/login",
            new LoginRequest($"nobody-{Guid.NewGuid():N}@test.invalid", "whatever-password"));
        var deletedResponse = await SendAsync(client, HttpMethod.Post, "api/v1/Auth/login",
            new LoginRequest(deletedAccount.Email.Value, deletedPassword));
        var wrongPasswordResponse = await SendAsync(client, HttpMethod.Post, "api/v1/Auth/login",
            new LoginRequest(liveAccount.Email.Value, "totally-wrong-password"));

        string unknownBody = await unknownResponse.Content.ReadAsStringAsync();
        string deletedBody = await deletedResponse.Content.ReadAsStringAsync();
        string wrongPasswordBody = await wrongPasswordResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, unknownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, deletedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordResponse.StatusCode);
        Assert.Contains("Invalid credentials", unknownBody);
        Assert.Equal(unknownBody, deletedBody);
        Assert.Equal(unknownBody, wrongPasswordBody);
    }

    [Fact]
    public async Task Login_Valid_IssuesTokensCarryingAccountIdAndRoleClaims()
    {
        using var client = _fixture.CreateAnonymousClient();
        var (account, password) = await SeedAccountAsync();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Auth/login", new LoginRequest(account.Email.Value, password));
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var auth = await response.Content.ReadFromJsonAsync<JsonElement>();
        string accessToken = auth.GetProperty("accessToken").GetString()!;
        Assert.False(string.IsNullOrEmpty(auth.GetProperty("refreshToken").GetString()));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        Assert.Equal(account.Id.ToString(), jwt.Claims.Single(c => c.Type == "accountId").Value);
        Assert.Equal(domain.Role.User.ToString(), jwt.Claims.Single(c => c.Type == "role").Value);

        long liveTokens = await CountLiveRefreshTokensAsync(account.Id);
        Assert.Equal(1, liveTokens);
    }

    // ---------------------------------------------------------------- #18 POST /Auth/refresh

    [Fact]
    public async Task Refresh_MissingBody_Returns415()
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Auth/refresh");

        await AssertStatusAsync(response, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Refresh_EmptyToken_Returns400()
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Auth/refresh", new RequestNewAccessTokenRequest(""));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "RefreshToken is required");
    }

    [Fact]
    public async Task Refresh_UnknownToken_Returns401()
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Auth/refresh", new RequestNewAccessTokenRequest("no-such-token"));

        await AssertStatusAsync(response, HttpStatusCode.Unauthorized, "Invalid refresh token");
    }

    [Fact]
    public async Task Refresh_ExpiredToken_Returns401()
    {
        var (account, _) = await SeedAccountAsync();
        RefreshToken expired = await SeedRefreshTokenAsync(account.Id, expiryDate: DateTime.UtcNow.AddDays(-1));
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Auth/refresh", new RequestNewAccessTokenRequest(expired.Token));

        await AssertStatusAsync(response, HttpStatusCode.Unauthorized, "Expired");
    }

    [Fact]
    public async Task Refresh_DeletedAccount_Returns404()
    {
        var (account, _) = await SeedAccountAsync();
        RefreshToken token = await SeedRefreshTokenAsync(account.Id);

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        await dbContext.GetCollection<Account>(CollectionName.Account).UpdateOneAsync(
            x => x.Id == account.Id,
            Builders<Account>.Update.Set(x => x.IsDeleted, true).Set(x => x.DeletedAt, DateTime.UtcNow));

        using var client = _fixture.CreateAnonymousClient();
        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Auth/refresh", new RequestNewAccessTokenRequest(token.Token));

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Account not found");
    }

    [Fact]
    public async Task Refresh_Valid_RotatesToken_OldTokenNoLongerUsable()
    {
        var (account, _) = await SeedAccountAsync();
        RefreshToken original = await SeedRefreshTokenAsync(account.Id);
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Auth/refresh", new RequestNewAccessTokenRequest(original.Token));
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var auth = await response.Content.ReadFromJsonAsync<JsonElement>();
        string newRefreshToken = auth.GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(original.Token, newRefreshToken);

        RefreshToken? storedOriginal = await FindRefreshTokenAsync(original.Id);
        Assert.True(storedOriginal!.IsRevoked);

        var reuse = await SendAsync(client, HttpMethod.Post, "api/v1/Auth/refresh", new RequestNewAccessTokenRequest(original.Token));
        await AssertStatusAsync(reuse, HttpStatusCode.Unauthorized, "Invalid refresh token");
    }

    // The highest-value test in the plan (test-strategy.md §9's framing): presenting an
    // already-revoked token is treated as theft, not ordinary reuse, and revokes every
    // other live session on the account — not just refuses the one request.
    [Fact]
    public async Task Refresh_RevokedTokenReuse_RevokesEveryLiveSessionOnTheAccount()
    {
        var (account, _) = await SeedAccountAsync();
        RefreshToken stolen = await SeedRefreshTokenAsync(account.Id, revoked: true);
        RefreshToken otherLiveSession = await SeedRefreshTokenAsync(account.Id);
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Auth/refresh", new RequestNewAccessTokenRequest(stolen.Token));

        await AssertStatusAsync(response, HttpStatusCode.Unauthorized, "Invalid refresh token");

        RefreshToken? storedOther = await FindRefreshTokenAsync(otherLiveSession.Id);
        Assert.True(storedOther!.IsRevoked);

        long liveTokens = await CountLiveRefreshTokensAsync(account.Id);
        Assert.Equal(0, liveTokens);
    }

    // Two requests race to rotate the same still-live token. The revoke write's own
    // filter (IsRevoked == false) is the concurrency control: only the request that wins
    // the write gets a fresh pair, the loser sees the row already revoked and is refused —
    // never two independent fresh pairs from one token.
    [Fact]
    public async Task Refresh_ConcurrentUseOfSameToken_OnlyOneRequestSucceeds()
    {
        var (account, _) = await SeedAccountAsync();
        RefreshToken token = await SeedRefreshTokenAsync(account.Id);

        var responses = await Task.WhenAll(
            SendAsync(_fixture.CreateAnonymousClient(), HttpMethod.Post, "api/v1/Auth/refresh", new RequestNewAccessTokenRequest(token.Token)),
            SendAsync(_fixture.CreateAnonymousClient(), HttpMethod.Post, "api/v1/Auth/refresh", new RequestNewAccessTokenRequest(token.Token)));

        int successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        int unauthorizedCount = responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized);

        Assert.Equal(1, successCount);
        Assert.Equal(1, unauthorizedCount);
    }

    // ---------------------------------------------------------------- #19 GET /Auth/refresh-token

    [Fact]
    public async Task GetAllRefreshToken_Valid_ReturnsPage()
    {
        var (admin, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);
        var (account, _) = await SeedAccountAsync();
        RefreshToken token = await SeedRefreshTokenAsync(account.Id);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Auth/refresh-token");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(page.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == token.Id);
    }

    // ---------------------------------------------------------------- #20 GET /Auth/refresh-token/{id}

    [Fact]
    public async Task GetSpecificRefreshToken_NotFound_Returns404()
    {
        var (admin, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Auth/refresh-token/{Guid.NewGuid()}");

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "RefreshToken not found");
    }

    [Fact]
    public async Task GetSpecificRefreshToken_Valid_ReturnsIt()
    {
        var (admin, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);
        var (account, _) = await SeedAccountAsync();
        RefreshToken token = await SeedRefreshTokenAsync(account.Id);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Auth/refresh-token/{token.Id}");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(token.Id, dto.GetProperty("id").GetGuid());
        Assert.Equal(account.Id, dto.GetProperty("accountId").GetGuid());
    }

    // ---------------------------------------------------------------- #75 POST /Auth/refresh-token/{id}/revoke

    [Fact]
    public async Task RevokeRefreshToken_NotFound_Returns404()
    {
        var (admin, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);

        var response = await SendAsync(client, HttpMethod.Post, $"api/v1/Auth/refresh-token/{Guid.NewGuid()}/revoke");

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Refresh token not found");
    }

    [Fact]
    public async Task RevokeRefreshToken_AlreadyRevoked_Returns409()
    {
        var (admin, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);
        var (account, _) = await SeedAccountAsync();
        RefreshToken token = await SeedRefreshTokenAsync(account.Id, revoked: true);

        var response = await SendAsync(client, HttpMethod.Post, $"api/v1/Auth/refresh-token/{token.Id}/revoke");

        await AssertStatusAsync(response, HttpStatusCode.Conflict, "Refresh token has been revoked");
    }

    [Fact]
    public async Task RevokeRefreshToken_Valid_TheTokenCanNoLongerRefresh()
    {
        var (admin, _) = await SeedAccountAsync();
        using var adminClient = AuthenticatedClientFor(admin, domain.Role.Admin);
        var (account, _) = await SeedAccountAsync();
        RefreshToken token = await SeedRefreshTokenAsync(account.Id);

        var revokeResponse = await SendAsync(adminClient, HttpMethod.Post, $"api/v1/Auth/refresh-token/{token.Id}/revoke");
        await AssertStatusAsync(revokeResponse, HttpStatusCode.NoContent);

        RefreshToken? stored = await FindRefreshTokenAsync(token.Id);
        Assert.True(stored!.IsRevoked);

        using var anonymousClient = _fixture.CreateAnonymousClient();
        var refreshResponse = await SendAsync(anonymousClient, HttpMethod.Post, "api/v1/Auth/refresh", new RequestNewAccessTokenRequest(token.Token));
        await AssertStatusAsync(refreshResponse, HttpStatusCode.Unauthorized, "Invalid refresh token");
    }
}
