using System.Net;

namespace integration;

// Stubs the three external SaaS adapters (Resend, Apple Maps, APNs) — all registered
// with AddHttpClient<,> in infra/DependencyInjection.cs — while leaving their real
// adapter code under test, per test-strategy.md §2.3. Routed by host rather than one
// handler per client because ConfigureHttpClientDefaults applies to every HttpClient the
// host builds, including the unnamed one MapsTokenProvider gets from
// IHttpClientFactory.CreateClient().
public sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.NotImplemented);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Respond(request));
    }
}
