using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using app;
using Microsoft.Extensions.Logging;

namespace infra;

// Resolves an Apple Maps place identifier into the record Apple holds for it.
//
// The identifier arrives from the client, but nothing else does: the name, address and
// coordinate a shop is created with are whatever this call answers. That is the entire
// guarantee — a client can claim any name it likes, but it cannot invent an identifier
// Apple will return a place for.
public sealed class ApplePlaceService : IPlaceService
{
    private const string PlaceEndpoint = "https://maps-api.apple.com/v1/place/";

    // Shop.Name and Shop.Address are both capped at 200 characters by CreatePostValidator.
    // A genuine Apple place that formats longer than that is truncated rather than
    // rejected: a rejection here is not something the person posting could ever fix.
    private const int MaxFieldLength = 200;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MapsTokenProvider _tokenProvider;
    private readonly ILogger<ApplePlaceService> _logger;

    public ApplePlaceService(
        HttpClient httpClient,
        MapsTokenProvider tokenProvider,
        ILogger<ApplePlaceService> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public async Task<PlaceResult?> ResolveAsync(string placeId, CancellationToken cancellationToken)
    {
        string? token = await _tokenProvider.CurrentAsync(cancellationToken);
        if(token is null)
            return null;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            PlaceEndpoint + Uri.EscapeDataString(placeId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        // An identifier Apple has never issued is an ordinary outcome, not a fault: the
        // client is one app version behind, or the place has been retired. Anything else
        // is ours to look at — a 401 is a bad key, a 429 is the daily quota.
        if(!response.IsSuccessStatusCode)
        {
            if(response.StatusCode != HttpStatusCode.NotFound)
                _logger.LogWarning(
                    "Maps place lookup returned {Status}: {Body}",
                    (int)response.StatusCode,
                    await response.Content.ReadAsStringAsync(cancellationToken));

            return null;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        var place = ReadPlace(body);

        if(place?.Name is null || place.Coordinate is null)
        {
            _logger.LogWarning("Maps place lookup returned an unusable place: {Body}", body);

            return null;
        }

        // formattedAddressLines is a mailing label split across lines; the single line the
        // form and the 200-character column expect is the same join the client already
        // uses on its own placemarks.
        string address = Truncate(string.Join(", ", place.FormattedAddressLines ?? []));

        if(string.IsNullOrWhiteSpace(address))
        {
            _logger.LogWarning("Maps place {PlaceId} has no address lines", placeId);

            return null;
        }

        return new PlaceResult(
            placeId,
            Truncate(place.Name),
            address,
            place.Coordinate.Latitude,
            place.Coordinate.Longitude);
    }

    // The single-identifier endpoint answers with a bare Place and the multi-identifier
    // one wraps the same object in `results`. Accepting either costs three lines and
    // removes the last place where this file has to be right about a response shape it
    // cannot exercise without credentials.
    private static ApplePlace? ReadPlace(string body)
    {
        var place = JsonSerializer.Deserialize<ApplePlace>(body, SerializerOptions);

        if(place?.Name is not null)
            return place;

        return JsonSerializer
            .Deserialize<ApplePlaceResults>(body, SerializerOptions)
            ?.Results
            ?.FirstOrDefault();
    }

    private static string Truncate(string value) =>
        value.Length <= MaxFieldLength ? value : value[..MaxFieldLength];

    private sealed record ApplePlace(
        string? Name,
        AppleCoordinate? Coordinate,
        [property: JsonPropertyName("formattedAddressLines")] string[]? FormattedAddressLines);

    private sealed record AppleCoordinate(double Latitude, double Longitude);

    private sealed record ApplePlaceResults(ApplePlace[]? Results);
}
