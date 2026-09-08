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
    private Shop() {}
    private Shop(
        string name,
        string address,
        double? latitude,
        double? longitude)
    {
        Name = name;
        Address = address;
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
        double? longitude = null)
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

        Shop shop = new Shop(name, address, latitude, longitude);

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
