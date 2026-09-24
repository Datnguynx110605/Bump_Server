using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace integration;

// test-strategy.md §3.6.3: one test per edge RateLimiterPolicy, asserting the limit, the 429
// shape and that partitioning actually isolates budgets — two IPs (auth, email) or two
// accounts (search, report, upload) must not share one. The global 200/min limiter is keyed
// the same way as auth/email (ResolveClientKey falls back to ip:{addr} for an anonymous
// request), so it is exercised anonymously too.
//
// Corrected against the actual runtime behaviour while writing these tests, not just against
// ConfigureRateLimiting's source: OnRejected reads the Retry-After value from
// Lease.TryGetMetadata(MetadataName.RetryAfter, ...), and System.Threading.RateLimiting's
// SlidingWindowRateLimiter — auth, email, search, report and upload all use it — never
// populates that metadata on a rejected lease, confirmed against the library directly. Only
// FixedWindowRateLimiter (the global limiter) does. So a 429 from the five named policies
// carries a ProblemDetails body with no Retry-After header; only the global one carries both.
[Collection(IntegrationCollection.Name)]
public class RateLimiterPolicyTests
{
    private readonly IntegrationTestFixture _fixture;

    public RateLimiterPolicyTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task AssertRateLimitedAsync(HttpResponseMessage response, bool expectRetryAfterHeader)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(expectRetryAfterHeader, response.Headers.TryGetValues("Retry-After", out var values) && values.Any());

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(StatusCodes.Status429TooManyRequests, problem!.Status);
        Assert.Equal("Too Many Requests", problem.Title);
    }

    [Fact]
    public async Task Auth_TenPermittedPerMinute_ByIP_EleventhIsRateLimited_AndADifferentIPIsUnaffected()
    {
        var client = _fixture.CreateAnonymousClient();

        for(int i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync(
                "api/v1/Auth/login",
                new LoginRequest("nobody@test.invalid", "wrong-password"));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        var eleventh = await client.PostAsJsonAsync(
            "api/v1/Auth/login",
            new LoginRequest("nobody@test.invalid", "wrong-password"));
        await AssertRateLimitedAsync(eleventh, expectRetryAfterHeader: false);

        var otherIpClient = _fixture.CreateAnonymousClient();
        var fromOtherIp = await otherIpClient.PostAsJsonAsync(
            "api/v1/Auth/login",
            new LoginRequest("nobody@test.invalid", "wrong-password"));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, fromOtherIp.StatusCode);
    }

    [Fact]
    public async Task Email_FivePermittedPerMinute_ByIP_SixthIsRateLimited_AndADifferentIPIsUnaffected()
    {
        var client = _fixture.CreateAnonymousClient();

        // A fresh email per call keeps OtpService's own per-address 5-per-hour cap (a
        // different, already-tested mechanism — see EmailEndpointTests) from being what
        // trips here: every call below is a first-ever generation for its address, so any
        // 429 this test sees can only be the edge policy counting by IP.
        for(int i = 0; i < 5; i++)
        {
            var response = await client.PostAsJsonAsync(
                "api/v1/Email/send-for-register",
                new SendEmailRequest($"rate-limit-test-{Guid.NewGuid():N}@test.invalid"));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        var sixth = await client.PostAsJsonAsync(
            "api/v1/Email/send-for-register",
            new SendEmailRequest($"rate-limit-test-{Guid.NewGuid():N}@test.invalid"));
        await AssertRateLimitedAsync(sixth, expectRetryAfterHeader: false);

        var otherIpClient = _fixture.CreateAnonymousClient();
        var fromOtherIp = await otherIpClient.PostAsJsonAsync(
            "api/v1/Email/send-for-register",
            new SendEmailRequest($"rate-limit-test-{Guid.NewGuid():N}@test.invalid"));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, fromOtherIp.StatusCode);
    }

    [Fact]
    public async Task Search_TwentyPermittedPerMinute_ByAccount_TwentyFirstIsRateLimited_AndADifferentAccountIsUnaffected()
    {
        var (client, _, _) = await _fixture.CreateAuthenticatedClientAsync();

        for(int i = 0; i < 20; i++)
        {
            var response = await client.GetAsync($"api/v1/Shop/search?keyword=cafe{i}");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        var twentyFirst = await client.GetAsync("api/v1/Shop/search?keyword=cafe-over-budget");
        await AssertRateLimitedAsync(twentyFirst, expectRetryAfterHeader: false);

        var (otherClient, _, _) = await _fixture.CreateAuthenticatedClientAsync();
        var fromOtherAccount = await otherClient.GetAsync("api/v1/Shop/search?keyword=cafe-fresh-budget");
        Assert.NotEqual(HttpStatusCode.TooManyRequests, fromOtherAccount.StatusCode);
    }

    [Fact]
    public async Task Report_TenPermittedPerMinute_ByAccount_EleventhIsRateLimited_AndADifferentAccountIsUnaffected()
    {
        var (client, _, _) = await _fixture.CreateAuthenticatedClientAsync();
        var body = new CreateReportRequest(null, null, Guid.NewGuid(), "spam");

        for(int i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync("api/v1/Report", body);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        var eleventh = await client.PostAsJsonAsync("api/v1/Report", body);
        await AssertRateLimitedAsync(eleventh, expectRetryAfterHeader: false);

        var (otherClient, _, _) = await _fixture.CreateAuthenticatedClientAsync();
        var fromOtherAccount = await otherClient.PostAsJsonAsync("api/v1/Report", body);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, fromOtherAccount.StatusCode);
    }

    private static MultipartFormDataContent AvatarForm()
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "avatar", "a.png");

        return form;
    }

    [Fact]
    public async Task Upload_TenPermittedPerMinute_ByAccount_EleventhIsRateLimited_AndADifferentAccountIsUnaffected()
    {
        var (client, _, _) = await _fixture.CreateAuthenticatedClientAsync();

        // Deliberately too small to pass ImageUpload.Validate: the rejection is a fast 400
        // that never touches R2, and — since the rate limiter runs before the controller
        // action — still consumes a permit regardless of that later 400.
        for(int i = 0; i < 10; i++)
        {
            var response = await client.PostAsync("api/v1/Account/me/avatar", AvatarForm());
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        var eleventh = await client.PostAsync("api/v1/Account/me/avatar", AvatarForm());
        await AssertRateLimitedAsync(eleventh, expectRetryAfterHeader: false);

        var (otherClient, _, _) = await _fixture.CreateAuthenticatedClientAsync();
        var fromOtherAccount = await otherClient.PostAsync("api/v1/Account/me/avatar", AvatarForm());
        Assert.NotEqual(HttpStatusCode.TooManyRequests, fromOtherAccount.StatusCode);
    }

    [Fact]
    public async Task Global_TwoHundredPermittedPerMinute_ByIP_TwoHundredAndFirstIsRateLimited()
    {
        var client = _fixture.CreateAnonymousClient();

        for(int i = 0; i < 200; i++)
        {
            var response = await client.GetAsync("health");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        var overBudget = await client.GetAsync("health");
        await AssertRateLimitedAsync(overBudget, expectRetryAfterHeader: true);

        var otherIpClient = _fixture.CreateAnonymousClient();
        var fromOtherIp = await otherIpClient.GetAsync("health");
        Assert.NotEqual(HttpStatusCode.TooManyRequests, fromOtherIp.StatusCode);
    }
}
