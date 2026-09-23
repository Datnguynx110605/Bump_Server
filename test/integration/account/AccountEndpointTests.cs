using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MongoDB.Driver;

namespace integration;

// test-strategy.md §3.6: every Account endpoint's whole input space, not just a happy path
// plus auth (the auth tier itself is already the AuthTierSweepTests table; the malformed
// {id:guid} case is RouteSweepTests). Each validator's own rule set is already pinned
// exhaustively at the U tier in test/unit/app/account, so the tables here carry one
// representative case per layer (§3.6.1) rather than repeating every validator rule —
// their job is to prove the rule is wired to the endpoint, not to re-pin it.
[Collection(IntegrationCollection.Name)]
public class AccountEndpointTests
{
    private readonly IntegrationTestFixture _fixture;

    public AccountEndpointTests(IntegrationTestFixture fixture)
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
    // this codebase owns, so those rows pass bodyFragment: null — the status code is the
    // contract worth pinning there. Every layer-2/3 row (the app's own messages) always
    // asserts a fragment.
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

    private async Task<string> MintRegistrationTokenAsync(string email)
    {
        using var scope = _fixture.Services.CreateScope();
        var registrationService = scope.ServiceProvider.GetRequiredService<IRegistrationService>();

        return await registrationService.GenerateTokenAsync(email, CancellationToken.None);
    }

    private async Task<(Account Account, string Password)> SeedAccountAsync(
        string? email = null,
        string? name = null,
        string password = "correct-password",
        string? bio = null)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var bcryptService = scope.ServiceProvider.GetRequiredService<IBcryptService>();

        Account account = Account.Create(
            Email.Create(email ?? $"acct-{Guid.NewGuid():N}@test.invalid").Value!,
            Name.Create(name ?? $"acct{Guid.NewGuid():N}"[..20]).Value!,
            bcryptService.HashPassword(password),
            bio).Value!;

        await dbContext.GetCollection<Account>(CollectionName.Account).InsertOneAsync(account);

        return (account, password);
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

    private async Task<Account?> FindAccountAsync(Guid id)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        return await dbContext.GetCollection<Account>(CollectionName.Account)
            .Find(x => x.Id == id)
            .FirstOrDefaultAsync();
    }

    private static readonly byte[] FakePng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private static MultipartFormDataContent AvatarForm(byte[] bytes, string fileName = "a.png")
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "avatar", fileName);

        return form;
    }

    // ---------------------------------------------------------------- #1 POST /Account

    public static TheoryData<string, object?, HttpStatusCode, string?> CreateAccountRows()
    {
        const string bogusToken = "no-such-registration-token";

        return new()
        {
            { "missing body", null, HttpStatusCode.UnsupportedMediaType, null },
            { "empty json object", new RawBody("{}"), HttpStatusCode.BadRequest, null },
            { "malformed json", new RawBody("{"), HttpStatusCode.BadRequest, null },
            { "empty registrationToken", new CreateAccountRequest("", "validname", "validpassword123", null), HttpStatusCode.BadRequest, "RegistrationToken is required" },
            { "password too short", new CreateAccountRequest(bogusToken, "validname", "short1", null), HttpStatusCode.BadRequest, "Password is too short, minimum length is 10 characters" },
            { "bio too long", new CreateAccountRequest(bogusToken, "validname", "validpassword123", new string('a', 2001)), HttpStatusCode.BadRequest, "Bio is too long, maximum length is 2000 characters" },
            { "invalid name (uppercase)", new CreateAccountRequest(bogusToken, "InvalidName", "validpassword123", null), HttpStatusCode.BadRequest, "Name is invalid" }
        };
    }

    [Theory]
    [MemberData(nameof(CreateAccountRows))]
    public async Task CreateAccount_InputMatrix(string description, object? body, HttpStatusCode expected, string? fragment)
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Account", body);

        await AssertStatusAsync(response, expected, fragment, description);
    }

    // Two independent validator failures in one request — the response shape itself is
    // what §3.6.2 flags as only observable here, not from status alone.
    [Fact]
    public async Task CreateAccount_TwoFieldsInvalid_ReturnsValidationProblemDetails()
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Account",
            new CreateAccountRequest("", "validname", "short1", null));

        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"errors\"", body);
    }

    [Fact]
    public async Task CreateAccount_UnknownToken_Returns401()
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Account",
            new CreateAccountRequest("no-such-token", "validname", "validpassword123", null));

        await AssertStatusAsync(response, HttpStatusCode.Unauthorized, "Unauthenticated");
    }

    [Fact]
    public async Task CreateAccount_DuplicateEmail_Returns409_AndTokenStaysReusable()
    {
        string email = $"dup-{Guid.NewGuid():N}@test.invalid";
        await SeedAccountAsync(email: email);
        string token = await MintRegistrationTokenAsync(email);
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Account",
            new CreateAccountRequest(token, "otherlname", "validpassword123", null));

        await AssertStatusAsync(response, HttpStatusCode.Conflict, "Email already exists");

        using var scope = _fixture.Services.CreateScope();
        var registrationService = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
        Assert.Equal(email, await registrationService.ExtractEmailAsync(token, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAccount_Valid_HashesPassword_AndConsumesTokenOnlyAfterCommit()
    {
        string email = $"create-{Guid.NewGuid():N}@test.invalid";
        string token = await MintRegistrationTokenAsync(email);
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Account",
            new CreateAccountRequest(token, "validname", "validpassword123", "hello"));

        await AssertStatusAsync(response, HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        Guid id = dto.GetProperty("id").GetGuid();

        Account? stored = await FindAccountAsync(id);
        Assert.NotNull(stored);
        Assert.Equal(60, stored!.Password.Length);

        using var scope = _fixture.Services.CreateScope();
        var registrationService = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
        Assert.Null(await registrationService.ExtractEmailAsync(token, CancellationToken.None));
    }

    // ---------------------------------------------------------------- #2 GET /Account/me

    [Fact]
    public async Task GetMyAccount_Valid_ReturnsOwnAccount_AndCachesIt()
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Account/me");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        using var scope = _fixture.Services.CreateScope();
        var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();
        var cached = await redisService.GetAsync<AccountSummaryDto>(CacheKey.Account(account.Id), CancellationToken.None);

        Assert.NotNull(cached);
        Assert.Equal(account.Id, cached!.Id);
    }

    // The row above only proves the cache gets written; it would pass even if
    // GetMyAccountHandler's own cache-read branch (GetMyAccountHandler.cs:44) never fired.
    // Mutating Mongo directly after the first call and asserting the second call still
    // returns the pre-mutation value is what actually pins "served from cache on the
    // second call" — a fresh read would see the mutation.
    [Fact]
    public async Task GetMyAccount_SecondCall_IsServedFromCache_NotReReadFromMongo()
    {
        var (account, _) = await SeedAccountAsync(bio: "original bio");
        using var client = AuthenticatedClientFor(account);

        var first = await SendAsync(client, HttpMethod.Get, "api/v1/Account/me");
        await AssertStatusAsync(first, HttpStatusCode.OK);

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        await dbContext.GetCollection<Account>(CollectionName.Account).UpdateOneAsync(
            x => x.Id == account.Id,
            Builders<Account>.Update.Set(x => x.Bio, "changed directly in mongo"));

        var second = await SendAsync(client, HttpMethod.Get, "api/v1/Account/me");
        await AssertStatusAsync(second, HttpStatusCode.OK);

        var dto = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("original bio", dto.GetProperty("bio").GetString());
    }

    // ---------------------------------------------------------------- #3 PUT /Account/me/name

    public static TheoryData<string, ChangeNameRequest?, HttpStatusCode, string?> ChangeNameRows() => new()
    {
        { "valid", new ChangeNameRequest("freshname"), HttpStatusCode.NoContent, null },
        { "missing body", null, HttpStatusCode.UnsupportedMediaType, null },
        { "empty name", new ChangeNameRequest(""), HttpStatusCode.BadRequest, "Name is required" },
        { "uppercase name", new ChangeNameRequest("InvalidName"), HttpStatusCode.BadRequest, "Name is invalid" },
        { "name over 200 chars", new ChangeNameRequest(new string('a', 201)), HttpStatusCode.BadRequest, "Name is invalid" },
        { "name at 200 chars", new ChangeNameRequest(new string('a', 200)), HttpStatusCode.NoContent, null }
    };

    [Theory]
    [MemberData(nameof(ChangeNameRows))]
    public async Task ChangeName_InputMatrix(string description, ChangeNameRequest? body, HttpStatusCode expected, string? fragment)
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Put, "api/v1/Account/me/name", body);

        await AssertStatusAsync(response, expected, fragment, description);
    }

    // Corrects test-strategy.md §5 row 3: ChangeNameHandler wraps the domain refusal in
    // AppError.Conflict, so this is 409, not the 400 the row previously claimed.
    [Fact]
    public async Task ChangeName_IdenticalValue_Returns409Conflict()
    {
        var (account, _) = await SeedAccountAsync(name: "samename");
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Put, "api/v1/Account/me/name", new ChangeNameRequest("samename"));

        await AssertStatusAsync(response, HttpStatusCode.Conflict, "New name must be different");
    }

    [Fact]
    public async Task ChangeName_Valid_PublishesProfileChanged()
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Put, "api/v1/Account/me/name", new ChangeNameRequest("freshnametwo"));
        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.ProfileChanged && m.Message is ProfileMessage pm && pm.AccountId == account.Id);
    }

    // ---------------------------------------------------------------- #4 PUT /Account/me/bio

    // Every row also asserts the stored Bio afterwards — a status-only assertion here
    // would pass identically whether the handler wrote the new value, ignored it, or
    // wrote the literal string "null".
    public static TheoryData<string, ChangeBioRequest, HttpStatusCode, string?, string?> ChangeBioRows() => new()
    {
        { "valid", new ChangeBioRequest("hello"), HttpStatusCode.NoContent, null, "hello" },
        { "null clears bio", new ChangeBioRequest(null), HttpStatusCode.NoContent, null, null },
        { "too long", new ChangeBioRequest(new string('a', 2001)), HttpStatusCode.BadRequest, "Bio is too long, maximum length is 2000 characters", "previous bio" },
        { "at 2000-char limit", new ChangeBioRequest(new string('a', 2000)), HttpStatusCode.NoContent, null, new string('a', 2000) }
    };

    [Theory]
    [MemberData(nameof(ChangeBioRows))]
    public async Task ChangeBio_InputMatrix(string description, ChangeBioRequest body, HttpStatusCode expected, string? fragment, string? expectedStoredBio)
    {
        var (account, _) = await SeedAccountAsync(bio: "previous bio");
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Put, "api/v1/Account/me/bio", body);

        await AssertStatusAsync(response, expected, fragment, description);

        Account? stored = await FindAccountAsync(account.Id);
        Assert.Equal(expectedStoredBio, stored!.Bio);
    }

    // ---------------------------------------------------------------- #5 PUT /Account/me/password

    [Fact]
    public async Task ChangePassword_MissingBody_Returns415()
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Put, "api/v1/Account/me/password");

        await AssertStatusAsync(response, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task ChangePassword_NewPasswordTooShort_Returns400()
    {
        var (account, password) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Put, "api/v1/Account/me/password",
            new ChangePasswordRequest(password, "short1"));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "New password is too short, minimum length is 10 characters");
    }

    // Deliberately inconsistent with the 401s around it (Result.Fail with no AppError, so
    // ResultStatusMapper's default applies) — test-strategy.md §5 row 5 calls this out by
    // name precisely because it looks like a bug.
    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_Returns400NotUnauthorized()
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Put, "api/v1/Account/me/password",
            new ChangePasswordRequest("totally-wrong-password", "brandnewpassword"));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Current password is incorrect");
    }

    [Fact]
    public async Task ChangePassword_UnchangedPassword_Returns409()
    {
        var (account, password) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Put, "api/v1/Account/me/password",
            new ChangePasswordRequest(password, password));

        await AssertStatusAsync(response, HttpStatusCode.Conflict, "New password must be different");
    }

    [Fact]
    public async Task ChangePassword_Valid_RevokesEveryRefreshToken_AndReturnsFreshPair()
    {
        var (account, password) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var refreshTokenCollection = dbContext.GetCollection<RefreshToken>(CollectionName.RefreshToken);
        RefreshToken staleToken = RefreshToken.Create(account.Id).Value!;
        await refreshTokenCollection.InsertOneAsync(staleToken);

        var response = await SendAsync(client, HttpMethod.Put, "api/v1/Account/me/password",
            new ChangePasswordRequest(password, "brandnewpassword"));

        await AssertStatusAsync(response, HttpStatusCode.OK);

        var auth = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(auth.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrEmpty(auth.GetProperty("refreshToken").GetString()));

        long liveTokens = await refreshTokenCollection.CountDocumentsAsync(
            x => x.AccountId == account.Id && !x.IsRevoked);
        // Exactly the fresh pair the response just handed back — the pre-seeded stale
        // token above must not survive the change.
        Assert.Equal(1, liveTokens);
    }

    // ---------------------------------------------------------------- #6 PUT /Account/me/email

    [Fact]
    public async Task ChangeEmail_MissingBody_Returns415()
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Put, "api/v1/Account/me/email");

        await AssertStatusAsync(response, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task ChangeEmail_WrongCurrentPassword_Returns401()
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);
        string token = await MintRegistrationTokenAsync($"new-{Guid.NewGuid():N}@test.invalid");

        var response = await SendAsync(client, HttpMethod.Put, "api/v1/Account/me/email",
            new ChangeEmailRequest(token, "totally-wrong-password"));

        await AssertStatusAsync(response, HttpStatusCode.Unauthorized, "Current password is incorrect");
    }

    [Fact]
    public async Task ChangeEmail_DuplicateEmail_Returns409()
    {
        string takenEmail = $"taken-{Guid.NewGuid():N}@test.invalid";
        await SeedAccountAsync(email: takenEmail);
        var (account, password) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);
        string token = await MintRegistrationTokenAsync(takenEmail);

        var response = await SendAsync(client, HttpMethod.Put, "api/v1/Account/me/email",
            new ChangeEmailRequest(token, password));

        await AssertStatusAsync(response, HttpStatusCode.Conflict, "Email already exists");
    }

    [Fact]
    public async Task ChangeEmail_Valid_UpdatesRow_AndConsumesToken()
    {
        var (account, password) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);
        string newEmail = $"new-{Guid.NewGuid():N}@test.invalid";
        string token = await MintRegistrationTokenAsync(newEmail);

        var response = await SendAsync(client, HttpMethod.Put, "api/v1/Account/me/email",
            new ChangeEmailRequest(token, password));

        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        Account? stored = await FindAccountAsync(account.Id);
        Assert.Equal(newEmail, stored!.Email.Value);

        using var scope = _fixture.Services.CreateScope();
        var registrationService = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
        Assert.Null(await registrationService.ExtractEmailAsync(token, CancellationToken.None));
    }

    // ---------------------------------------------------------------- #7 POST /Account/me/avatar

    [Theory]
    [InlineData("empty file", new byte[0], "a.png", "File is empty")]
    [InlineData("wrong extension", new byte[] { 1, 2, 3 }, "a.txt", "File type is not supported")]
    [InlineData("content does not match extension", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, "a.png", "File content does not match a supported image format")]
    public async Task ChangeAvatar_InputMatrix(string description, byte[] bytes, string fileName, string fragment)
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await client.PostAsync("api/v1/Account/me/avatar", AvatarForm(bytes, fileName));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, fragment, description);
    }

    [Fact]
    public async Task ChangeAvatar_WrongContentType_Returns415()
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Account/me/avatar", new { avatar = "not-a-file" });

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task ChangeAvatar_Valid_UploadsToR2_AndDeletesThePreviousObject()
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var first = await client.PostAsync("api/v1/Account/me/avatar", AvatarForm(FakePng));
        await AssertStatusAsync(first, HttpStatusCode.NoContent);

        Account? afterFirst = await FindAccountAsync(account.Id);
        string firstUrl = afterFirst!.Avatars.Single().Url;

        using var scope = _fixture.Services.CreateScope();
        var s3 = scope.ServiceProvider.GetRequiredService<Amazon.S3.IAmazonS3>();
        var listAfterFirst = await s3.ListObjectsV2Async(new Amazon.S3.Model.ListObjectsV2Request { BucketName = "bump-test" });
        Assert.Contains(listAfterFirst.S3Objects, o => firstUrl.Contains(o.Key));

        var second = await client.PostAsync("api/v1/Account/me/avatar", AvatarForm(FakePng, "b.png"));
        await AssertStatusAsync(second, HttpStatusCode.NoContent);

        Account? afterSecond = await FindAccountAsync(account.Id);
        Assert.Single(afterSecond!.Avatars);
        Assert.NotEqual(firstUrl, afterSecond.Avatars.Single().Url);

        var listAfterSecond = await s3.ListObjectsV2Async(new Amazon.S3.Model.ListObjectsV2Request { BucketName = "bump-test" });
        Assert.DoesNotContain(listAfterSecond.S3Objects, o => firstUrl.Contains(o.Key));

        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.ProfileChanged && m.Message is ProfileMessage pm && pm.AccountId == account.Id);
    }

    // ---------------------------------------------------------------- #8 DELETE /Account/me/avatar

    [Fact]
    public async Task RemoveAvatar_MissingUrlQueryParam_Returns400()
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Delete, "api/v1/Account/me/avatar");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RemoveAvatar_UrlNotOwnedByCaller_Returns404()
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Delete, "api/v1/Account/me/avatar?url=http://bump-test-public.invalid/posts/not-mine.png");

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Avatar not found");
    }

    [Fact]
    public async Task RemoveAvatar_Valid_RemovesTheR2Object()
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);
        await client.PostAsync("api/v1/Account/me/avatar", AvatarForm(FakePng));

        Account? withAvatar = await FindAccountAsync(account.Id);
        string url = withAvatar!.Avatars.Single().Url;

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Account/me/avatar?url={Uri.EscapeDataString(url)}");
        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        Account? afterRemove = await FindAccountAsync(account.Id);
        Assert.Empty(afterRemove!.Avatars);

        using var scope = _fixture.Services.CreateScope();
        var s3 = scope.ServiceProvider.GetRequiredService<Amazon.S3.IAmazonS3>();
        var list = await s3.ListObjectsV2Async(new Amazon.S3.Model.ListObjectsV2Request { BucketName = "bump-test" });
        Assert.DoesNotContain(list.S3Objects ?? [], o => url.Contains(o.Key));

        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.ProfileChanged && m.Message is ProfileMessage pm && pm.AccountId == account.Id);
    }

    // ---------------------------------------------------------------- #9 DELETE /Account/me

    [Fact]
    public async Task DeleteAccount_Valid_SoftDeletesAndPublishesCascadeMessage()
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Delete, "api/v1/Account/me");
        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        Account? stored = await FindAccountAsync(account.Id);
        Assert.True(stored!.IsDeleted);
        Assert.NotNull(stored.DeletedAt);

        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.DeleteAccount && m.Message is AccountMessage am && am.AccountId == account.Id);
    }

    // ---------------------------------------------------------------- #10 POST /Account/recover

    [Fact]
    public async Task RecoverAccount_MissingBody_Returns415()
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Account/recover");

        await AssertStatusAsync(response, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task RecoverAccount_UnknownToken_Returns401()
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Account/recover", new RecoverAccountRequest("no-such-token"));

        await AssertStatusAsync(response, HttpStatusCode.Unauthorized, "Unauthenticated");
    }

    [Fact]
    public async Task RecoverAccount_AccountStillLive_Returns404()
    {
        var (account, _) = await SeedAccountAsync();
        string token = await MintRegistrationTokenAsync(account.Email.Value);
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Account/recover", new RecoverAccountRequest(token));

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Account not found");
    }

    [Fact]
    public async Task RecoverAccount_Valid_ClearsDeletedFlag()
    {
        var (account, _) = await SeedAccountAsync();
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var accountCollection = dbContext.GetCollection<Account>(CollectionName.Account);
        await accountCollection.UpdateOneAsync(
            x => x.Id == account.Id,
            Builders<Account>.Update.Set(x => x.IsDeleted, true).Set(x => x.DeletedAt, DateTime.UtcNow));

        string token = await MintRegistrationTokenAsync(account.Email.Value);
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Account/recover", new RecoverAccountRequest(token));
        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        Account? stored = await FindAccountAsync(account.Id);
        Assert.False(stored!.IsDeleted);
    }

    // ---------------------------------------------------------------- #11 PUT /Account/reset-password

    [Fact]
    public async Task ResetPassword_MissingBody_Returns415()
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Put, "api/v1/Account/reset-password");

        await AssertStatusAsync(response, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task ResetPassword_NewPasswordTooShort_Returns400()
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Put, "api/v1/Account/reset-password",
            new ResetPasswordRequest("no-such-token", "short1"));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "New password is too short, minimum length is 10 characters");
    }

    [Fact]
    public async Task ResetPassword_Valid_UpdatesHash_AndTokenIsSingleUse()
    {
        var (account, _) = await SeedAccountAsync();
        string token = await MintRegistrationTokenAsync(account.Email.Value);
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Put, "api/v1/Account/reset-password",
            new ResetPasswordRequest(token, "brandnewpassword"));
        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        using var scope = _fixture.Services.CreateScope();
        var bcryptService = scope.ServiceProvider.GetRequiredService<IBcryptService>();
        Account? stored = await FindAccountAsync(account.Id);
        Assert.True(bcryptService.VerifyPassword("brandnewpassword", stored!.Password));

        var registrationService = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
        Assert.Null(await registrationService.ExtractEmailAsync(token, CancellationToken.None));
    }

    // ---------------------------------------------------------------- #12 GET /Account

    [Fact]
    public async Task GetAllAccount_ExcludesSoftDeleted_AndClampsPageSize()
    {
        var (admin, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);

        var (deleted, _) = await SeedAccountAsync();
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        await dbContext.GetCollection<Account>(CollectionName.Account).UpdateOneAsync(
            x => x.Id == deleted.Id,
            Builders<Account>.Update.Set(x => x.IsDeleted, true).Set(x => x.DeletedAt, DateTime.UtcNow));

        // pageSize=999999 clamps silently to 100 rather than rejecting — the opposite of
        // the validated-range endpoints, per test-strategy.md §3.6.2.
        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Account?pageSize=999999");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = page.GetProperty("items");
        Assert.DoesNotContain(items.EnumerateArray(), item => item.GetProperty("id").GetGuid() == deleted.Id);
    }

    // ---------------------------------------------------------------- #13 GET /Account/{accountId}

    [Fact]
    public async Task GetSpecificAccount_WellFormedGuid_NoRow_Returns404()
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Account/{Guid.NewGuid()}");

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Account not found");
    }

    [Fact]
    public async Task GetSpecificAccount_Valid_ReturnsTheAccount()
    {
        var (viewer, _) = await SeedAccountAsync();
        var (target, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Account/{target.Id}");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(target.Id, dto.GetProperty("id").GetGuid());
    }

    // ---------------------------------------------------------------- #14 GET /Account/by-email

    [Fact]
    public async Task GetAccountByEmail_MissingQueryParam_Returns400()
    {
        var (admin, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Account/by-email");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetAccountByEmail_MalformedEmail_Returns400()
    {
        var (admin, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Account/by-email?email=not-an-email");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Email is invalid");
    }

    [Fact]
    public async Task GetAccountByEmail_NoMatch_Returns404()
    {
        var (admin, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Account/by-email?email=nobody@test.invalid");

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Account not found");
    }

    [Fact]
    public async Task GetAccountByEmail_Valid_ReturnsTheAccount()
    {
        var (admin, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);
        var (target, _) = await SeedAccountAsync();

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Account/by-email?email={Uri.EscapeDataString(target.Email.Value)}");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(target.Id, dto.GetProperty("id").GetGuid());
    }

    // ---------------------------------------------------------------- #15 GET /Account/by-name

    [Fact]
    public async Task GetAccountByName_MissingQueryParam_Returns400()
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Account/by-name");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetAccountByName_UppercaseName_Returns400()
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Account/by-name?name=NotLowercase");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Name is invalid");
    }

    // Corrects test-strategy.md §5 row 15: this is Filter.Eq on the stored (already
    // lowercase-only) Name, not a TextNormalizer-based fold — that behaviour belongs to
    // /Account/search (row 16). A near-match with different casing therefore finds
    // nothing rather than matching, and — unlike GetSpecificAccount — a list endpoint
    // answers an empty page rather than 404 when nothing matches.
    [Fact]
    public async Task GetAccountByName_NoExactMatch_ReturnsEmptyPage_Not404()
    {
        var (viewer, _) = await SeedAccountAsync();
        await SeedAccountAsync(name: "somename");
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Account/by-name?name=othername");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(page.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task GetAccountByName_ExactMatch_ReturnsIt()
    {
        var (viewer, _) = await SeedAccountAsync();
        var (target, _) = await SeedAccountAsync(name: "exactmatchname");
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Account/by-name?name=exactmatchname");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(page.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == target.Id);
    }

    // ---------------------------------------------------------------- #16 GET /Account/search

    [Fact]
    public async Task GetAccountByKeyword_MissingQueryParam_Returns400()
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Account/search");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetAccountByKeyword_KeywordTooLong_Returns400()
    {
        var (account, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Account/search?keyword={new string('a', 101)}");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Keyword maximum length is 100 characters");
    }

    [Fact]
    public async Task GetAccountByKeyword_NormalizedMatch_FindsRegardlessOfSubstringPosition()
    {
        var (viewer, _) = await SeedAccountAsync();
        var (target, _) = await SeedAccountAsync(name: $"prefix{Guid.NewGuid():N}suffix"[..30]);
        using var client = AuthenticatedClientFor(viewer);

        string middleSlice = target.Name.Value[3..10];
        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Account/search?keyword={middleSlice}");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(page.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == target.Id);
    }

    // Pins a real gap, not a rule: NameTests.LowercaseDiacritic_IsAccepted shows
    // Name.Create stores diacritics as-is, but GetAccountByKeywordHandler only normalizes
    // the incoming keyword, never the stored field — so a diacritic name is unreachable by
    // any substring search that doesn't happen to type the exact diacritics back. This is
    // a production gap (fix belongs in Name.Create or the stored field, its own approval
    // under AGENTS.md, same as the queue-boundary correlation-id gap in §7.3), recorded
    // here so nobody "fixes" the handler in a way that quietly changes this.
    [Fact]
    public async Task GetAccountByKeyword_DiacriticName_IsNotFoundByItsNormalizedForm()
    {
        var (viewer, _) = await SeedAccountAsync();
        var (target, _) = await SeedAccountAsync(name: $"càphê{Guid.NewGuid():N}"[..20]);
        using var client = AuthenticatedClientFor(viewer);

        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Account/search?keyword=caphe");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(page.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == target.Id);
    }

    // ---------------------------------------------------------------- #72 DELETE /Account/admin/{accountId}

    [Fact]
    public async Task AdminDeleteAccount_OwnAccount_Returns400()
    {
        var (admin, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Account/admin/{admin.Id}");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Use the self-service endpoint");
    }

    [Fact]
    public async Task AdminDeleteAccount_TargetNotFound_Returns404()
    {
        var (admin, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Account/admin/{Guid.NewGuid()}");

        await AssertStatusAsync(response, HttpStatusCode.NotFound, "Account not found");
    }

    [Fact]
    public async Task AdminDeleteAccount_Valid_SoftDeletesTheTarget()
    {
        var (admin, _) = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(admin, domain.Role.Admin);
        var (target, _) = await SeedAccountAsync();

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Account/admin/{target.Id}");
        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        Account? stored = await FindAccountAsync(target.Id);
        Assert.True(stored!.IsDeleted);

        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.DeleteAccount && m.Message is AccountMessage am && am.AccountId == target.Id);
    }
}
