namespace unit;

public class ShopTests
{
    [Fact]
    public void Create_EmptyName_IsRejected()
    {
        DomainResult<Shop> result = Shop.Create(string.Empty, "123 Main St");

        Assert.False(result.IsSuccess);
        Assert.Equal("Shop name is required", result.Error);
    }

    [Fact]
    public void Create_EmptyAddress_IsRejected()
    {
        DomainResult<Shop> result = Shop.Create("Shop", string.Empty);

        Assert.False(result.IsSuccess);
        Assert.Equal("Shop address is required", result.Error);
    }

    [Fact]
    public void Create_LatitudeWithoutLongitude_IsRejected()
    {
        DomainResult<Shop> result = Shop.Create("Shop", "Address", latitude: 10);

        Assert.False(result.IsSuccess);
        Assert.Equal("Latitude and longitude must be provided together", result.Error);
    }

    [Fact]
    public void Create_LongitudeWithoutLatitude_IsRejected()
    {
        DomainResult<Shop> result = Shop.Create("Shop", "Address", longitude: 10);

        Assert.False(result.IsSuccess);
        Assert.Equal("Latitude and longitude must be provided together", result.Error);
    }

    [Theory]
    [InlineData(-90.1)]
    [InlineData(90.1)]
    public void Create_LatitudeOutOfRange_IsRejected(double latitude)
    {
        DomainResult<Shop> result = Shop.Create("Shop", "Address", latitude, 0);

        Assert.False(result.IsSuccess);
        Assert.Equal("Latitude must be between -90 and 90", result.Error);
    }

    [Theory]
    [InlineData(-90)]
    [InlineData(90)]
    public void Create_LatitudeAtBoundary_IsAccepted(double latitude)
    {
        DomainResult<Shop> result = Shop.Create("Shop", "Address", latitude, 0);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(-180.1)]
    [InlineData(180.1)]
    public void Create_LongitudeOutOfRange_IsRejected(double longitude)
    {
        DomainResult<Shop> result = Shop.Create("Shop", "Address", 0, longitude);

        Assert.False(result.IsSuccess);
        Assert.Equal("Longitude must be between -180 and 180", result.Error);
    }

    [Theory]
    [InlineData(-180)]
    [InlineData(180)]
    public void Create_LongitudeAtBoundary_IsAccepted(double longitude)
    {
        DomainResult<Shop> result = Shop.Create("Shop", "Address", 0, longitude);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_WithCoordinates_StoresLocationAsLongitudeThenLatitude()
    {
        DomainResult<Shop> result = Shop.Create("Shop", "Address", latitude: 21.0, longitude: 105.8);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { 105.8, 21.0 }, result.Value!.Location);
    }

    [Fact]
    public void Create_WithoutCoordinates_LocationIsNull()
    {
        DomainResult<Shop> result = Shop.Create("Shop", "Address");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Location);
    }

    [Fact]
    public void Create_NormalizesNameAndAddress()
    {
        DomainResult<Shop> result = Shop.Create("Phở Hà Nội", "Đà Nẵng");

        Assert.True(result.IsSuccess);
        Assert.Equal("pho ha noi", result.Value!.NameNormalized);
        Assert.Equal("da nang", result.Value.AddressNormalized);
    }

    [Fact]
    public void AdoptPlace_EmptyPlaceId_IsRejected()
    {
        Shop shop = Shop.Create("Shop", "Address").Value!;

        DomainResult result = shop.AdoptPlace(string.Empty);

        Assert.False(result.IsSuccess);
        Assert.Equal("PlaceId is required", result.Error);
    }

    [Fact]
    public void AdoptPlace_AlreadyLinked_IsRejected()
    {
        Shop shop = Shop.Create("Shop", "Address", placeId: "place-1").Value!;

        DomainResult result = shop.AdoptPlace("place-2");

        Assert.False(result.IsSuccess);
        Assert.Equal("Shop is already linked to a place", result.Error);
    }

    [Fact]
    public void AdoptPlace_EmptySlot_Succeeds()
    {
        Shop shop = Shop.Create("Shop", "Address").Value!;

        DomainResult result = shop.AdoptPlace("place-1");

        Assert.True(result.IsSuccess);
        Assert.Equal("place-1", shop.PlaceId);
    }

    [Fact]
    public void ChangeRate_SetsRate()
    {
        Shop shop = Shop.Create("Shop", "Address").Value!;
        Amount newRate = Amount.Create(4).Value!;

        DomainResult result = shop.ChangeRate(newRate);

        Assert.True(result.IsSuccess);
        Assert.Same(newRate, shop.Rate);
    }

    [Fact]
    public void DeleteShop_MarksDeleted()
    {
        Shop shop = Shop.Create("Shop", "Address").Value!;

        DomainResult result = shop.DeleteShop();

        Assert.True(result.IsSuccess);
        Assert.True(shop.IsDeleted);
    }
}
