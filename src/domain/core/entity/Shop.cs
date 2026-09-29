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
    public double[] Location { get; private set; }
    // The place identifier the maps provider knows this shop by, and the shop's whole
    // identity: every other field beside it was read back from the provider under this
    // identifier rather than accepted from the client, which is what makes a shop
    // navigable. A shop the provider has no record of cannot be created at all.
    public string PlaceId { get; private set; }
    private Shop() {}
    private Shop(
        string name,
        string address,
        double latitude,
        double longitude,
        string placeId)
    {
        Name = name;
        Address = address;
        PlaceId = placeId;
        NameNormalized = TextNormalizer.RemoveDiacriticsAndLower(name);
        AddressNormalized = TextNormalizer.RemoveDiacriticsAndLower(address);
        Location = new[] { longitude, latitude };
    }

    public static DomainResult<Shop> Create(
        string name,
        string address,
        double latitude,
        double longitude,
        string placeId)
    {
        if(string.IsNullOrWhiteSpace(name))
            return DomainResult<Shop>.Failure("Shop name is required");

        if(string.IsNullOrWhiteSpace(address))
            return DomainResult<Shop>.Failure("Shop address is required");

        if(string.IsNullOrWhiteSpace(placeId))
            return DomainResult<Shop>.Failure("PlaceId is required");

        // Kept although the pair now only ever arrives from the provider: that provider is
        // an external boundary, and a coordinate Mongo cannot index is a 500 at insert time
        // rather than a message anybody can act on.
        if(latitude is < -90 or > 90)
            return DomainResult<Shop>.Failure("Latitude must be between -90 and 90");

        if(longitude is < -180 or > 180)
            return DomainResult<Shop>.Failure("Longitude must be between -180 and 180");

        Shop shop = new Shop(name, address, latitude, longitude, placeId);

        return DomainResult<Shop>.Success(shop);
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
