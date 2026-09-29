using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace infra;

// Mints the access token every Apple Maps Server API call authenticates with. Two steps
// rather than the one APNs needs: an ES256 JWT signed with the Maps private key is
// exchanged at /v1/token for a short-lived access token, and only the access token is
// accepted by the API itself.
//
// A singleton for the same reason ApnsTokenProvider is one — the exchange is a network
// round trip billed against a shared 25,000-call daily quota, so its result has to be
// cached process-wide instead of per request.
public sealed class MapsTokenProvider
{
    private const string TokenEndpoint = "https://maps-api.apple.com/v1/token";

    // The signed JWT only has to outlive the single exchange it is sent with.
    private static readonly TimeSpan AuthTokenLifetime = TimeSpan.FromMinutes(5);
    // Apple reports the access token's life in expiresInSeconds, 1800 at the time of
    // writing. Refreshing a minute early keeps a call that starts just before expiry from
    // arriving just after it.
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MapsTokenProvider> _logger;
    private readonly string? _keyId;
    private readonly string? _teamId;
    private readonly string? _privateKey;
    // The exchange is asynchronous, so the token cache cannot be guarded by a lock.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _accessToken;
    private DateTime _expiresAt;

    private bool IsConfigured => _keyId is not null && _teamId is not null && _privateKey is not null;

    // Configuration is read eagerly but not demanded, the one deliberate difference from
    // ApnsTokenProvider. Post creation is not the whole API: a deployment without a Maps key
    // still serves every read, every other mutation and every post about a shop the catalogue
    // already holds — only a post about a new shop fails — and throwing here would take all
    // of that down with it. The startup warning is what keeps "unconfigured" from being
    // mistaken for "Apple rejected it".
    public MapsTokenProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<MapsTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _keyId = Optional(configuration, "Maps:KeyId");
        _teamId = Optional(configuration, "Maps:TeamId");
        _privateKey = Optional(configuration, "Maps:PrivateKey");

        if(!IsConfigured)
            _logger.LogWarning(
                "Maps is not configured; posts naming a place identifier will be rejected");
    }

    public async Task<string?> CurrentAsync(CancellationToken cancellationToken)
    {
        if(!IsConfigured)
            return null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if(_accessToken is not null && DateTime.UtcNow < _expiresAt)
                return _accessToken;

            using var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, TokenEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SignAuthToken());

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, cancellationToken);
            }
            // Same network conditions ApplePlaceService.ResolveAsync guards against: a
            // timeout or dropped connection here should degrade to "can't verify right now"
            // rather than crash the request with a raw 500.
            catch(Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogError(ex, "Maps token exchange timed out or failed");

                return null;
            }

            using(response)
            {
                if(!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "Maps token exchange returned {Status}: {Body}",
                        (int)response.StatusCode,
                        await response.Content.ReadAsStringAsync(cancellationToken));

                    return null;
                }

                var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);

                if(string.IsNullOrWhiteSpace(token?.AccessToken))
                {
                    _logger.LogError("Maps token exchange returned no access token");

                    return null;
                }

                _accessToken = token.AccessToken;
                // Clamped: an absent or nonsensical lifetime would otherwise expire the token
                // on arrival and spend a quota call on every single request.
                _expiresAt = DateTime.UtcNow
                    .AddSeconds(Math.Max(token.ExpiresInSeconds, 60))
                    .Subtract(ExpiryMargin);

                return _accessToken;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // Same ES256 construction as the APNs provider token, with the two claims the Maps
    // service adds: an expiry, and the scope that authorizes the server API specifically.
    // No origin claim — that one is only required for the browser-facing scopes.
    private string SignAuthToken()
    {
        using ECDsa key = ECDsa.Create();
        key.ImportFromPem(Encoding.UTF8.GetString(Convert.FromBase64String(_privateKey!)));

        string header = Base64UrlEncoder.Encode($"{{\"alg\":\"ES256\",\"kid\":\"{_keyId}\",\"typ\":\"JWT\"}}");

        long issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long expiresAt = DateTimeOffset.UtcNow.Add(AuthTokenLifetime).ToUnixTimeSeconds();

        string claims = Base64UrlEncoder.Encode(
            $"{{\"iss\":\"{_teamId}\",\"iat\":{issuedAt},\"exp\":{expiresAt},\"scope\":\"server_api\"}}");

        byte[] signature = key.SignData(
            Encoding.UTF8.GetBytes($"{header}.{claims}"),
            HashAlgorithmName.SHA256);

        // JWS ES256 is the IEEE P1363 r||s pair, exactly 64 bytes for P-256. A DER
        // signature is 70-72 bytes and variable, and the service answers one with an
        // opaque 401 that gives no hint the encoding is the problem.
        if(signature.Length != 64)
            throw new InvalidOperationException("Maps requires a 64-byte P1363 signature, not DER");

        return $"{header}.{claims}.{Base64UrlEncoder.Encode(signature)}";
    }

    private static string? Optional(IConfiguration configuration, string key)
    {
        string? value = configuration[key];

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("accessToken")] string? AccessToken,
        [property: JsonPropertyName("expiresInSeconds")] int ExpiresInSeconds);
}
