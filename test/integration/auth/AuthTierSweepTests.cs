using System.Net;
using System.Net.Http.Headers;

namespace integration;

// test-strategy.md §3.6.3: one theory over all 83 routes, asserting the tier the
// [Authorize]/[AllowAnonymous] attribute on each action claims — an attribute is exactly
// what a refactor drops silently, and this is the one test that would notice. The
// expected tier per route is taken from test-strategy.md §5 (itself derived from
// features.md's Auth column), not from reading the attributes here — otherwise the test
// would just be asserting the attribute against itself and could never fail.
[Collection(IntegrationCollection.Name)]
public class AuthTierSweepTests
{
    public enum AuthTier { Anonymous, User, Admin }

    public sealed record RouteCase(HttpMethod Method, string Route, AuthTier Tier)
    {
        public override string ToString() => $"{Method} {Route} ({Tier})";
    }

    private readonly IntegrationTestFixture _fixture;

    public AuthTierSweepTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    public static IEnumerable<object[]> Routes()
    {
        (HttpMethod Method, string Route, AuthTier Tier)[] rows =
        [
            // Account (16, + the admin cascade delete which features.md counts under
            // "Administrator additions")
            (HttpMethod.Post, "api/v1/Account", AuthTier.Anonymous),
            (HttpMethod.Get, "api/v1/Account/me", AuthTier.User),
            (HttpMethod.Put, "api/v1/Account/me/name", AuthTier.User),
            (HttpMethod.Put, "api/v1/Account/me/bio", AuthTier.User),
            (HttpMethod.Put, "api/v1/Account/me/password", AuthTier.User),
            (HttpMethod.Put, "api/v1/Account/me/email", AuthTier.User),
            (HttpMethod.Post, "api/v1/Account/me/avatar", AuthTier.User),
            (HttpMethod.Delete, "api/v1/Account/me/avatar", AuthTier.User),
            (HttpMethod.Delete, "api/v1/Account/me", AuthTier.User),
            (HttpMethod.Post, "api/v1/Account/recover", AuthTier.Anonymous),
            (HttpMethod.Put, "api/v1/Account/reset-password", AuthTier.Anonymous),
            (HttpMethod.Get, "api/v1/Account", AuthTier.Admin),
            (HttpMethod.Get, "api/v1/Account/{guid}", AuthTier.User),
            (HttpMethod.Get, "api/v1/Account/by-email", AuthTier.Admin),
            (HttpMethod.Get, "api/v1/Account/by-name", AuthTier.User),
            (HttpMethod.Get, "api/v1/Account/search", AuthTier.User),
            (HttpMethod.Delete, "api/v1/Account/admin/{guid}", AuthTier.Admin),

            // Auth (5)
            (HttpMethod.Post, "api/v1/Auth/login", AuthTier.Anonymous),
            (HttpMethod.Post, "api/v1/Auth/refresh", AuthTier.Anonymous),
            (HttpMethod.Get, "api/v1/Auth/refresh-token", AuthTier.Admin),
            (HttpMethod.Get, "api/v1/Auth/refresh-token/{guid}", AuthTier.Admin),
            (HttpMethod.Post, "api/v1/Auth/refresh-token/{guid}/revoke", AuthTier.Admin),

            // Email (3)
            (HttpMethod.Post, "api/v1/Email/send-for-register", AuthTier.Anonymous),
            (HttpMethod.Post, "api/v1/Email/send-for-verify", AuthTier.Anonymous),
            (HttpMethod.Post, "api/v1/Email/verify", AuthTier.Anonymous),

            // Post (10, + the two admin-tier ones counted under "Administrator additions")
            (HttpMethod.Post, "api/v1/Post", AuthTier.User),
            (HttpMethod.Put, "api/v1/Post/{guid}/caption", AuthTier.User),
            (HttpMethod.Put, "api/v1/Post/{guid}/rate", AuthTier.User),
            (HttpMethod.Delete, "api/v1/Post/{guid}", AuthTier.User),
            (HttpMethod.Get, "api/v1/Post/{guid}", AuthTier.User),
            (HttpMethod.Get, "api/v1/Post/mine", AuthTier.User),
            (HttpMethod.Get, "api/v1/Post/account/{guid}", AuthTier.User),
            (HttpMethod.Get, "api/v1/Post/shop/{guid}", AuthTier.User),
            (HttpMethod.Get, "api/v1/Post/related", AuthTier.User),
            (HttpMethod.Get, "api/v1/Post/search", AuthTier.User),
            (HttpMethod.Get, "api/v1/Post", AuthTier.Admin),
            (HttpMethod.Delete, "api/v1/Post/admin/{guid}", AuthTier.Admin),

            // Comment (6, + 2 admin)
            (HttpMethod.Post, "api/v1/Comment", AuthTier.User),
            (HttpMethod.Put, "api/v1/Comment/{guid}", AuthTier.User),
            (HttpMethod.Delete, "api/v1/Comment/{guid}", AuthTier.User),
            (HttpMethod.Get, "api/v1/Comment/{guid}", AuthTier.User),
            (HttpMethod.Get, "api/v1/Comment/post/{guid}", AuthTier.User),
            (HttpMethod.Get, "api/v1/Comment/mine", AuthTier.User),
            (HttpMethod.Get, "api/v1/Comment", AuthTier.Admin),
            (HttpMethod.Delete, "api/v1/Comment/admin/{guid}", AuthTier.Admin),

            // Shop (7)
            (HttpMethod.Delete, "api/v1/Shop/{guid}", AuthTier.Admin),
            (HttpMethod.Get, "api/v1/Shop/{guid}", AuthTier.User),
            (HttpMethod.Get, "api/v1/Shop", AuthTier.User),
            (HttpMethod.Get, "api/v1/Shop/around", AuthTier.User),
            (HttpMethod.Get, "api/v1/Shop/search", AuthTier.User),
            (HttpMethod.Get, "api/v1/Shop/by-name", AuthTier.User),
            (HttpMethod.Get, "api/v1/Shop/by-address", AuthTier.User),

            // Reaction (6)
            (HttpMethod.Post, "api/v1/Reaction/post/{guid}", AuthTier.User),
            (HttpMethod.Delete, "api/v1/Reaction/post/{guid}", AuthTier.User),
            (HttpMethod.Post, "api/v1/Reaction/comment/{guid}", AuthTier.User),
            (HttpMethod.Delete, "api/v1/Reaction/comment/{guid}", AuthTier.User),
            (HttpMethod.Get, "api/v1/Reaction/post/{guid}", AuthTier.User),
            (HttpMethod.Get, "api/v1/Reaction/comment/{guid}", AuthTier.User),

            // Follow (6)
            (HttpMethod.Post, "api/v1/Follow/{guid}", AuthTier.User),
            (HttpMethod.Delete, "api/v1/Follow/{guid}", AuthTier.User),
            (HttpMethod.Get, "api/v1/Follow/follower/{guid}", AuthTier.User),
            (HttpMethod.Get, "api/v1/Follow/following/{guid}", AuthTier.User),
            (HttpMethod.Get, "api/v1/Follow/me/follower", AuthTier.User),
            (HttpMethod.Get, "api/v1/Follow/me/following", AuthTier.User),

            // Block (3)
            (HttpMethod.Post, "api/v1/Block/{guid}", AuthTier.User),
            (HttpMethod.Delete, "api/v1/Block/{guid}", AuthTier.User),
            (HttpMethod.Get, "api/v1/Block/me", AuthTier.User),

            // Save (6)
            (HttpMethod.Post, "api/v1/Save/post/{guid}", AuthTier.User),
            (HttpMethod.Delete, "api/v1/Save/post/{guid}", AuthTier.User),
            (HttpMethod.Post, "api/v1/Save/shop/{guid}", AuthTier.User),
            (HttpMethod.Delete, "api/v1/Save/shop/{guid}", AuthTier.User),
            (HttpMethod.Get, "api/v1/Save/me/post", AuthTier.User),
            (HttpMethod.Get, "api/v1/Save/me/shop", AuthTier.User),

            // Report (1, + 3 admin)
            (HttpMethod.Post, "api/v1/Report", AuthTier.User),
            (HttpMethod.Get, "api/v1/Report", AuthTier.Admin),
            (HttpMethod.Get, "api/v1/Report/{guid}", AuthTier.Admin),
            (HttpMethod.Get, "api/v1/Report/target", AuthTier.Admin),

            // Notification (4)
            (HttpMethod.Get, "api/v1/Notification/{guid}", AuthTier.User),
            (HttpMethod.Get, "api/v1/Notification/mine", AuthTier.User),
            (HttpMethod.Post, "api/v1/Notification/device", AuthTier.User),
            (HttpMethod.Delete, "api/v1/Notification/device/test-token", AuthTier.User),

            // Dashboard (2)
            (HttpMethod.Get, "api/v1/Dashboard/stats", AuthTier.Admin),
            (HttpMethod.Get, "api/v1/Dashboard/activity", AuthTier.Admin)
        ];

        if(rows.Length != 83)
            throw new InvalidOperationException($"Expected 83 routes, found {rows.Length}");

        return rows.Select(r => new object[] { new RouteCase(r.Method, r.Route, r.Tier) });
    }

    private static string ResolveRoute(string route) => route.Replace("{guid}", Guid.NewGuid().ToString());

    private HttpRequestMessage BuildRequest(RouteCase testCase)
    {
        return new HttpRequestMessage(testCase.Method, ResolveRoute(testCase.Route));
    }

    // A 500 passes both `NotEqual(401)` and `NotEqual(403)` below, which would hide a route
    // that is broken outright behind a green sweep — every response must clear this too.
    private static void AssertNotServerError(HttpResponseMessage response) =>
        Assert.True(
            (int)response.StatusCode < 500,
            $"{response.RequestMessage!.Method} {response.RequestMessage.RequestUri} -> {(int)response.StatusCode}");

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Anonymous_ReachesOnlyAllowAnonymousRoutes(RouteCase testCase)
    {
        // MintTokenAsync/RandomIp moved to TestAuth (fixture/TestAuth.cs) — every area's
        // endpoint tests need the same arrange step, not just this sweep.
        using var client = _fixture.CreateAnonymousClient();
        using var request = BuildRequest(testCase);

        var response = await client.SendAsync(request);

        AssertNotServerError(response);

        if(testCase.Tier == AuthTier.Anonymous)
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        else
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task UserToken_ReachesEveryTierExceptAdmin(RouteCase testCase)
    {
        var (client, _, _) = await _fixture.CreateAuthenticatedClientAsync(domain.Role.User);
        using var disposableClient = client;

        using var request = BuildRequest(testCase);
        var response = await client.SendAsync(request);

        AssertNotServerError(response);

        if(testCase.Tier == AuthTier.Admin)
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        else
        {
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task AdminToken_ReachesEveryRoute(RouteCase testCase)
    {
        var (client, _, _) = await _fixture.CreateAuthenticatedClientAsync(domain.Role.Admin);
        using var disposableClient = client;

        using var request = BuildRequest(testCase);
        var response = await client.SendAsync(request);

        AssertNotServerError(response);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
