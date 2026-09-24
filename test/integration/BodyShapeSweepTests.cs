using System.Net;
using System.Text;

namespace integration;

// test-strategy.md §3.6.3's third generic sweep: the absent/malformed-body case, over every
// endpoint that binds a JSON body. Reuses AuthTierSweepTests.Routes() for the route list and
// its tier, same as RouteSweepTests does for the malformed-guid sweep, rather than a second
// copy of the route table.
//
// "{}" is excluded for ChangeBio: ChangeBioRequest's only field is the nullable Bio, so an
// empty object deserializes to a request that legitimately clears the bio (204, confirmed in
// AccountEndpointTests' own input matrix) rather than failing at layer 1 — read while writing
// this sweep, corrected from an initial assumption that every endpoint here 400s on "{}".
[Collection(IntegrationCollection.Name)]
public class BodyShapeSweepTests
{
    private static readonly HashSet<(HttpMethod Method, string Route)> BodyRoutes =
    [
        (HttpMethod.Post, "api/v1/Account"),
        (HttpMethod.Put, "api/v1/Account/me/name"),
        (HttpMethod.Put, "api/v1/Account/me/bio"),
        (HttpMethod.Put, "api/v1/Account/me/password"),
        (HttpMethod.Put, "api/v1/Account/me/email"),
        (HttpMethod.Post, "api/v1/Account/recover"),
        (HttpMethod.Put, "api/v1/Account/reset-password"),
        (HttpMethod.Post, "api/v1/Auth/login"),
        (HttpMethod.Post, "api/v1/Auth/refresh"),
        (HttpMethod.Post, "api/v1/Email/send-for-register"),
        (HttpMethod.Post, "api/v1/Email/send-for-verify"),
        (HttpMethod.Post, "api/v1/Email/verify"),
        (HttpMethod.Post, "api/v1/Notification/device"),
        (HttpMethod.Put, "api/v1/Post/{guid}/caption"),
        (HttpMethod.Put, "api/v1/Post/{guid}/rate"),
        (HttpMethod.Put, "api/v1/Comment/{guid}"),
        (HttpMethod.Post, "api/v1/Report")
    ];

    private readonly IntegrationTestFixture _fixture;

    public BodyShapeSweepTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    public static IEnumerable<object[]> BodyEndpoints() =>
        AuthTierSweepTests.Routes()
            .Select(row => (AuthTierSweepTests.RouteCase)row[0]!)
            .Where(r => BodyRoutes.Contains((r.Method, r.Route)))
            .Select(r => new object[] { r });

    public static IEnumerable<object[]> BodyEndpointsWhereEmptyObjectIsInvalid() =>
        BodyEndpoints().Where(row => ((AuthTierSweepTests.RouteCase)row[0]!).Route != "api/v1/Account/me/bio");

    private async Task<HttpClient> ClientForAsync(AuthTierSweepTests.AuthTier tier) => tier switch
    {
        AuthTierSweepTests.AuthTier.Anonymous => _fixture.CreateAnonymousClient(),
        AuthTierSweepTests.AuthTier.User => (await _fixture.CreateAuthenticatedClientAsync()).Client,
        AuthTierSweepTests.AuthTier.Admin => (await _fixture.CreateAuthenticatedClientAsync(Role.Admin)).Client,
        _ => throw new ArgumentOutOfRangeException(nameof(tier))
    };

    private static string ResolveRoute(string route) => route.Replace("{guid}", Guid.NewGuid().ToString());

    [Theory]
    [MemberData(nameof(BodyEndpoints))]
    public async Task NoContentTypeHeader_Returns415(AuthTierSweepTests.RouteCase testCase)
    {
        var client = await ClientForAsync(testCase.Tier);
        using var request = new HttpRequestMessage(testCase.Method, ResolveRoute(testCase.Route))
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("{}"))
        };
        request.Content.Headers.ContentType = null;

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BodyEndpointsWhereEmptyObjectIsInvalid))]
    public async Task EmptyJsonObjectBody_Returns400(AuthTierSweepTests.RouteCase testCase)
    {
        var client = await ClientForAsync(testCase.Tier);

        var response = await client.SendAsync(new HttpRequestMessage(testCase.Method, ResolveRoute(testCase.Route))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BodyEndpoints))]
    public async Task TruncatedJsonBody_Returns400(AuthTierSweepTests.RouteCase testCase)
    {
        var client = await ClientForAsync(testCase.Tier);

        var response = await client.SendAsync(new HttpRequestMessage(testCase.Method, ResolveRoute(testCase.Route))
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json")
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
