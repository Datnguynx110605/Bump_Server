using System.Net;

namespace integration;

// test-strategy.md §3.6.3's second generic sweep: a malformed {id:guid} segment fails the
// route's own constraint, so routing never matches an endpoint and the request 404s before
// any filter or handler runs — regardless of the route's auth tier, since UseRouting
// resolves the endpoint before UseAuthentication/UseAuthorization ever see it. Reuses
// AuthTierSweepTests.Routes() rather than a second copy of the same 83-row table.
[Collection(IntegrationCollection.Name)]
public class RouteSweepTests
{
    private readonly IntegrationTestFixture _fixture;

    public RouteSweepTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    public static IEnumerable<object[]> RoutesWithGuidSegment() =>
        AuthTierSweepTests.Routes()
            .Select(row => (AuthTierSweepTests.RouteCase)row[0]!)
            .Where(r => r.Route.Contains("{guid}"))
            .Select(r => new object[] { r });

    [Theory]
    [MemberData(nameof(RoutesWithGuidSegment))]
    public async Task MalformedGuidSegment_404sFromRouting_RegardlessOfTier(AuthTierSweepTests.RouteCase testCase)
    {
        using var client = _fixture.CreateAnonymousClient();
        using var request = new HttpRequestMessage(
            testCase.Method,
            testCase.Route.Replace("{guid}", "not-a-guid"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
