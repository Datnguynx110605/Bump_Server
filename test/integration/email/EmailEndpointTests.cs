using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace integration;

// test-strategy.md §5 rows 21–23. All three actions share the class-level
// [EnableRateLimiting(RateLimiterPolicy.Email)] — 5/min, partitioned by IP
// (ResolveIp, not ResolveClientKey — these routes are always anonymous) — which is
// independent of OtpService's own per-email, 5/hour generation budget in
// infra/service/OtpService.cs. Row 21's "6th send in an hour → 429 from OtpService, not
// the edge limiter" only holds if the six requests don't share one edge partition first;
// CreateAnonymousClient's fresh IP per client is what keeps them apart.
[Collection(IntegrationCollection.Name)]
public class EmailEndpointTests
{
    private readonly IntegrationTestFixture _fixture;

    public EmailEndpointTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;

        // Every successful OTP generation reaches EmailService.SendAsync, which throws if
        // Resend answers anything but success — so any test that expects a send to succeed
        // needs this, the same way CreatePostHandlerTests configures the Maps stub.
        _fixture.HttpHandler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);
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

    private async Task<Account> SeedAccountAsync(string? email = null)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var bcryptService = scope.ServiceProvider.GetRequiredService<IBcryptService>();

        Account account = Account.Create(
            Email.Create(email ?? $"acct-{Guid.NewGuid():N}@test.invalid").Value!,
            Name.Create($"acct{Guid.NewGuid():N}"[..20]).Value!,
            bcryptService.HashPassword("correct-password"),
            bio: null).Value!;

        await dbContext.GetCollection<Account>(CollectionName.Account).InsertOneAsync(account);

        return account;
    }

    private async Task<string> GenerateOtpAsync(string email)
    {
        using var scope = _fixture.Services.CreateScope();
        var otpService = scope.ServiceProvider.GetRequiredService<IOtpService>();

        return (await otpService.GenerateAsync(email, CancellationToken.None))!;
    }

    // ---------------------------------------------------------------- #21 POST /Email/send-for-register

    public static TheoryData<string, object?, HttpStatusCode, string?> SendForRegisterRows() => new()
    {
        { "missing body", null, HttpStatusCode.UnsupportedMediaType, null },
        { "malformed json", new RawBody("{"), HttpStatusCode.BadRequest, null },
        { "empty email", new SendEmailRequest(""), HttpStatusCode.BadRequest, "Email is required" },
        { "malformed email (no @)", new SendEmailRequest("not-an-email"), HttpStatusCode.BadRequest, "Email is invalid" }
    };

    [Theory]
    [MemberData(nameof(SendForRegisterRows))]
    public async Task SendForRegister_InputMatrix(string description, object? body, HttpStatusCode expected, string? fragment)
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Email/send-for-register", body);

        await AssertStatusAsync(response, expected, fragment, description);
    }

    [Fact]
    public async Task SendForRegister_ExistingEmail_Returns409()
    {
        Account account = await SeedAccountAsync();
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Email/send-for-register", new SendEmailRequest(account.Email.Value));

        await AssertStatusAsync(response, HttpStatusCode.Conflict, "Email already exists");
    }

    [Fact]
    public async Task SendForRegister_NewEmail_Returns204()
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Email/send-for-register",
            new SendEmailRequest($"new-{Guid.NewGuid():N}@test.invalid"));

        await AssertStatusAsync(response, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SendForRegister_SixthSendInAnHour_Returns429FromOtpService_NotTheEdgeLimiter()
    {
        string email = $"throttled-{Guid.NewGuid():N}@test.invalid";
        HttpResponseMessage? sixth = null;

        for(int i = 0; i < 6; i++)
        {
            // A fresh client (and so a fresh IP, per CreateAnonymousClient) per request:
            // the 5/min edge policy is partitioned by IP, and six requests from one IP
            // would trip that first, making it indistinguishable from OtpService's own
            // 5/hour budget for this email. Six different IPs keep the edge partition
            // out of the way so only the per-email counter is under test.
            using var client = _fixture.CreateAnonymousClient();
            sixth = await SendAsync(client, HttpMethod.Post, "api/v1/Email/send-for-register", new SendEmailRequest(email));
        }

        string body = await sixth!.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        // The edge limiter's own rejection answers "Rate limit exceeded. Please retry
        // later." (api/DependencyInjection.cs ConfigureRateLimiting) — a different string
        // is what proves OtpService answered, not the edge.
        Assert.Contains("Too many verification requests for this address", body);
    }

    // ---------------------------------------------------------------- #22 POST /Email/send-for-verify

    public static TheoryData<string, object?, HttpStatusCode, string?> SendForVerifyRows() => new()
    {
        { "missing body", null, HttpStatusCode.UnsupportedMediaType, null },
        { "malformed json", new RawBody("{"), HttpStatusCode.BadRequest, null },
        { "empty email", new SendEmailRequest(""), HttpStatusCode.BadRequest, "Email is required" },
        { "malformed email (no @)", new SendEmailRequest("not-an-email"), HttpStatusCode.BadRequest, "Email is invalid" }
    };

    [Theory]
    [MemberData(nameof(SendForVerifyRows))]
    public async Task SendForVerify_InputMatrix(string description, object? body, HttpStatusCode expected, string? fragment)
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Email/send-for-verify", body);

        await AssertStatusAsync(response, expected, fragment, description);
    }

    // Corrects test-strategy.md §5 row 22: the doc said "unknown email refused", but
    // SendEmailForVerifyHandler's own comment explains this used to answer 404 and was
    // changed deliberately — an unknown address on the password-reset path must not be
    // distinguishable from a known one, so this is 204, not a refusal.
    [Fact]
    public async Task SendForVerify_UnknownEmail_Returns204_NotRefused()
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Email/send-for-verify",
            new SendEmailRequest($"nobody-{Guid.NewGuid():N}@test.invalid"));

        await AssertStatusAsync(response, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SendForVerify_ExistingEmail_Returns204()
    {
        Account account = await SeedAccountAsync();
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Email/send-for-verify", new SendEmailRequest(account.Email.Value));

        await AssertStatusAsync(response, HttpStatusCode.NoContent);
    }

    // The one deliberate exception to "every other read filters IsDeleted" in the
    // codebase: this is the OTP RecoverAccount exchanges for a registration token, so a
    // soft-deleted account must still reach the generator.
    [Fact]
    public async Task SendForVerify_SoftDeletedAccount_StillReturns204_NotSkippedLikeUnknown()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var bcryptService = scope.ServiceProvider.GetRequiredService<IBcryptService>();
        Account account = Account.Create(
            Email.Create($"deleted-{Guid.NewGuid():N}@test.invalid").Value!,
            Name.Create($"acct{Guid.NewGuid():N}"[..20]).Value!,
            bcryptService.HashPassword("correct-password"),
            bio: null).Value!;
        account.Delete();
        await dbContext.GetCollection<Account>(CollectionName.Account).InsertOneAsync(account);

        using var client = _fixture.CreateAnonymousClient();
        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Email/send-for-verify", new SendEmailRequest(account.Email.Value));

        await AssertStatusAsync(response, HttpStatusCode.NoContent);

        // Observable proof it actually generated, not just took the same 204 exit an
        // unknown address would: GenerateAsync itself does no account lookup, so calling
        // it directly always returns a code regardless of what the endpoint did — that
        // alone would pass either way. What can't lie is the shared per-email budget
        // (MaxGenerationsPerWindow = 5): if the endpoint's call counted, only 4 direct
        // calls are left before the budget is spent; if it took the unknown-address exit
        // instead, all 5 succeed.
        var otpService = scope.ServiceProvider.GetRequiredService<IOtpService>();
        var results = new List<string?>();
        for(int i = 0; i < 5; i++)
            results.Add(await otpService.GenerateAsync(account.Email.Value, CancellationToken.None));

        // 5 direct calls landing on top of the endpoint's own successful generation is
        // generation #6 overall — over budget — only if the endpoint's call counted.
        Assert.Null(results[4]);
    }

    // ---------------------------------------------------------------- #23 POST /Email/verify

    // "valid" carries a null body and a true sentinel, same as LoginRows — a real success
    // needs a code actually issued through GenerateOtpAsync, which MemberData can't do
    // (it runs synchronously, before the theory body's async arrange).
    public static TheoryData<string, object?, HttpStatusCode, string?, bool> VerifyRows() => new()
    {
        { "valid", null, HttpStatusCode.OK, null, true },
        { "missing body", null, HttpStatusCode.UnsupportedMediaType, null, false },
        { "malformed json", new RawBody("{"), HttpStatusCode.BadRequest, null, false },
        { "empty email", new VerifyEmailRequest("", "123456"), HttpStatusCode.BadRequest, "Email is required", false },
        { "malformed email (no @)", new VerifyEmailRequest("not-an-email", "123456"), HttpStatusCode.BadRequest, "Email is invalid", false },
        { "empty otp", new VerifyEmailRequest("someone@test.invalid", ""), HttpStatusCode.BadRequest, "Otp is required", false },
        { "otp too short", new VerifyEmailRequest("someone@test.invalid", "12345"), HttpStatusCode.BadRequest, "Otp is invalid", false },
        { "otp too long", new VerifyEmailRequest("someone@test.invalid", "1234567"), HttpStatusCode.BadRequest, "Otp is invalid", false }
    };

    [Theory]
    [MemberData(nameof(VerifyRows))]
    public async Task Verify_InputMatrix(string description, object? body, HttpStatusCode expected, string? fragment, bool seedRealOtp)
    {
        using var client = _fixture.CreateAnonymousClient();

        if(seedRealOtp)
        {
            string email = $"matrix-{Guid.NewGuid():N}@test.invalid";
            string code = await GenerateOtpAsync(email);
            body = new VerifyEmailRequest(email, code);
        }

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Email/verify", body);

        await AssertStatusAsync(response, expected, fragment, description);
    }

    [Fact]
    public async Task Verify_UnknownEmail_Returns400_OtpIsIncorrect()
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await SendAsync(client, HttpMethod.Post, "api/v1/Email/verify",
            new VerifyEmailRequest($"nobody-{Guid.NewGuid():N}@test.invalid", "123456"));

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Otp is incorrect");
    }

    // The third wrong guess is where OtpService.VerifyAsync removes the entry outright
    // (MaxAttempts = 3) rather than just recording another failure — so the *correct*
    // code, presented next, is refused too. That is the behaviour "refused" in
    // test-strategy.md §5 row 23 actually means: not a 429, a permanently spent code.
    [Fact]
    public async Task Verify_WrongCodeThreeTimes_ThenEvenTheCorrectCodeIsRefused()
    {
        string email = $"lockout-{Guid.NewGuid():N}@test.invalid";
        string correctCode = await GenerateOtpAsync(email);
        using var client = _fixture.CreateAnonymousClient();

        for(int i = 0; i < 3; i++)
        {
            var wrong = await SendAsync(client, HttpMethod.Post, "api/v1/Email/verify", new VerifyEmailRequest(email, "000000"));
            await AssertStatusAsync(wrong, HttpStatusCode.BadRequest, "Otp is incorrect", $"wrong attempt {i + 1}");
        }

        var afterLockout = await SendAsync(client, HttpMethod.Post, "api/v1/Email/verify", new VerifyEmailRequest(email, correctCode));
        await AssertStatusAsync(afterLockout, HttpStatusCode.BadRequest, "Otp is incorrect");
    }

    [Fact]
    public async Task Verify_CorrectCode_ReturnsRegistrationToken_AndStaysVerifiableWithinItsTtl()
    {
        string email = $"verify-{Guid.NewGuid():N}@test.invalid";
        string code = await GenerateOtpAsync(email);
        using var client = _fixture.CreateAnonymousClient();

        var first = await SendAsync(client, HttpMethod.Post, "api/v1/Email/verify", new VerifyEmailRequest(email, code));
        await AssertStatusAsync(first, HttpStatusCode.OK);
        string firstToken = await first.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrEmpty(firstToken));

        // Deliberate: a lost response must be recoverable, so a correct code is not
        // consumed on success (test-strategy.md §3.4's OtpService row).
        var second = await SendAsync(client, HttpMethod.Post, "api/v1/Email/verify", new VerifyEmailRequest(email, code));
        await AssertStatusAsync(second, HttpStatusCode.OK);
    }
}
