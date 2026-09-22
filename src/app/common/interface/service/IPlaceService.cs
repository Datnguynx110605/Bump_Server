namespace app;

// A place as the maps provider knows it. Every field here was read back from the provider
// rather than taken from the request: resolving an identifier the client cannot forge is
// the only thing that makes a shop's name, address and coordinate trustworthy.
public sealed record PlaceResult(
    string PlaceId,
    string Name,
    string Address,
    double Latitude,
    double Longitude);

public interface IPlaceService
{
    // Null when the identifier resolves to nothing — unknown to the provider, a provider
    // error, or no provider configured. The three are logged apart and answered the same,
    // because from the client's side they are the same thing: this place cannot be used.
    Task<PlaceResult?> ResolveAsync(string placeId, CancellationToken cancellationToken);
}
