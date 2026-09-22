namespace domain;

public sealed class Shop : BaseEntity
{
    public string Name { get; private set; }
    public string Address { get; private set; }
    public string NameNormalized { get; private set; }
    public string AddressNormalized { get; private set; }
    public Amount Rate { get; private set; } = Amount.Create(0).Value;
    // Stored as [longitude, latitude] — the order the 2dsphere index and every
    // $nearSphere query expect, and the reverse of how the pair is spoken about.
    // Null when the post was created without a place pick; the 2dsphere index skips
    // those documents, so a shop with no coordinate simply never appears in a
    // proximity search rather than appearing at (0, 0).
    public double[]? Location { get; private set; }
    // The place identifier the maps provider knows this shop by. Null for a shop somebody
    // typed in by hand, which is the whole difference between a verified shop and an
    // unverified one: a verified shop's name, address and coordinate were read back from
    // the provider rather than accepted from the client, so it can be navigated to.
    public string? PlaceId { get; private set; }
    private Shop() {}
    private Shop(
        string name,
        string address,
        double? latitude,
        double? longitude,
        string? placeId)
    {
        Name = name;
        Address = address;
        PlaceId = placeId;
        NameNormalized = TextNormalizer.RemoveDiacriticsAndLower(name);
        AddressNormalized = TextNormalizer.RemoveDiacriticsAndLower(address);
        Location = latitude is null || longitude is null
            ? null
            : new[] { longitude.Value, latitude.Value };
    }

    public static DomainResult<Shop> Create(
        string name,
        string address,
        double? latitude = null,
        double? longitude = null,
        string? placeId = null)
    {
        if(string.IsNullOrWhiteSpace(name))
            return DomainResult<Shop>.Failure("Shop name is required");

        if(string.IsNullOrWhiteSpace(address))
            return DomainResult<Shop>.Failure("Shop address is required");

        // A half-supplied pair is rejected rather than silently dropped: a coordinate
        // Mongo cannot index throws at insert time, which would surface as a 500.
        if((latitude is null) != (longitude is null))
            return DomainResult<Shop>.Failure("Latitude and longitude must be provided together");

        if(latitude is < -90 or > 90)
            return DomainResult<Shop>.Failure("Latitude must be between -90 and 90");

        if(longitude is < -180 or > 180)
            return DomainResult<Shop>.Failure("Longitude must be between -180 and 180");

        Shop shop = new Shop(name, address, latitude, longitude, placeId);

        return DomainResult<Shop>.Success(shop);
    }

    // A shop that predates place verification, matched on its name and address and
    // stamped with the identifier of the place that resolved to them. Only ever fills an
    // empty slot: once a shop carries an identifier that identifier is its identity, and
    // replacing it would silently repoint the row at a different place.
    public DomainResult AdoptPlace(string placeId)
    {
        if(string.IsNullOrWhiteSpace(placeId))
            return DomainResult.Failure("PlaceId is required");

        if(PlaceId is not null)
            return DomainResult.Failure("Shop is already linked to a place");

        PlaceId = placeId;

        return Update();
    }

    public DomainResult ChangeRate(Amount rate)
    {
        Rate = rate;

        return Update();
    }

    public DomainResult DeleteShop()
    {
        return Delete();
    }
}
