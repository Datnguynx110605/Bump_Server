using System.Net.Http.Json;
using Serilog.Events;

namespace integration;

// test-strategy.md §3.6.5: CorrelationIdMiddleware echoes a valid inbound id and replaces one
// that is over 64 characters or contains anything outside [a-zA-Z0-9-] — an injection guard on
// a value that reaches every log line, so the rejection cases carry more weight than the
// happy one. Hits /health rather than a feature endpoint: any request passes through the
// middleware, and health needs no auth or seeded data.
[Collection(IntegrationCollection.Name)]
public class CorrelationIdMiddlewareTests
{
    private const string HeaderName = "X-Correlation-ID";

    private readonly IntegrationTestFixture _fixture;

    public CorrelationIdMiddlewareTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<string> SendAndReadCorrelationIdAsync(string? inboundId)
    {
        using var client = _fixture.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "health");
        if(inboundId is not null)
            request.Headers.Add(HeaderName, inboundId);

        var response = await client.SendAsync(request);

        return Assert.Single(response.Headers.GetValues(HeaderName));
    }

    [Fact]
    public async Task ValidInboundId_IsEchoedBack()
    {
        string inboundId = "client-generated-id-123";

        string echoed = await SendAndReadCorrelationIdAsync(inboundId);

        Assert.Equal(inboundId, echoed);
    }

    [Fact]
    public async Task NoInboundId_GetsAFreshOne()
    {
        string generated = await SendAndReadCorrelationIdAsync(inboundId: null);

        Assert.True(Guid.TryParse(generated, out _));
    }

    [Fact]
    public async Task InboundIdOver64Characters_IsReplacedRatherThanEchoed()
    {
        string tooLong = new string('a', 65);

        string result = await SendAndReadCorrelationIdAsync(tooLong);

        Assert.NotEqual(tooLong, result);
        Assert.True(Guid.TryParse(result, out _));
    }

    [Theory]
    [InlineData("bad;id")]
    [InlineData("bad id")]
    [InlineData("bad/id")]
    [InlineData("<script>")]
    public async Task InboundIdWithDisallowedCharacters_IsReplacedRatherThanEchoed(string invalidId)
    {
        string result = await SendAndReadCorrelationIdAsync(invalidId);

        Assert.NotEqual(invalidId, result);
        Assert.True(Guid.TryParse(result, out _));
    }

    // test-strategy.md §7.3: the id CorrelationIdMiddleware pushes into Serilog's LogContext
    // must reach an event logged from inside the request, not only the response header.
    // LoggingBehavior logs "Handling {RequestName}"/"Handled {RequestName}" around every
    // MediatR command and query, so any endpoint proves it — login is anonymous and needs no
    // seeded account. CapturingLogEventSink is picked up by Program.cs's own
    // .ReadFrom.Services(services) call with no production change.
    [Fact]
    public async Task CorrelationId_ReachesALogEventEmittedFromInsideTheMediatRPipeline()
    {
        string correlationId = $"corr-{Guid.NewGuid():N}";
        using var client = _fixture.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/Auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("nobody@test.invalid", "wrong-password"))
        };
        request.Headers.Add(HeaderName, correlationId);

        await client.SendAsync(request);

        var matching = _fixture.LogEvents.Events.FirstOrDefault(e =>
            e.RenderMessage().Contains("LoginCommand") &&
            e.Properties.TryGetValue("CorrelationId", out var value) &&
            value is ScalarValue { Value: string idValue } &&
            idValue == correlationId);

        Assert.NotNull(matching);
    }
}
