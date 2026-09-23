namespace unit;

public class CreatePostValidatorTests
{
    private static readonly CreatePostValidator Validator = new();

    private static CreatePostCommand Command(
        string? placeId = null,
        string? shopName = "Cafe",
        string? shopAddress = "1 Main St",
        double? latitude = null,
        double? longitude = null,
        string? caption = null,
        int rate = 3,
        List<(Stream stream, string fileName)>? images = null) =>
        new(placeId, shopName, shopAddress, latitude, longitude, caption, Amount.Create(rate).Value!, images);

    [Fact]
    public void PlaceIdOverTwoHundredChars_IsRejected()
    {
        var result = Validator.Validate(Command(placeId: new string('a', 201)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "PlaceId is too long, maximum length is 200 characters");
    }

    [Fact]
    public void NoPlaceId_EmptyShopName_IsRejected()
    {
        var result = Validator.Validate(Command(shopName: ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "ShopName is required");
    }

    [Fact]
    public void NoPlaceId_EmptyShopAddress_IsRejected()
    {
        var result = Validator.Validate(Command(shopAddress: ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "ShopAddress is required");
    }

    [Fact]
    public void WithPlaceId_EmptyShopNameAndAddress_Passes()
    {
        var result = Validator.Validate(Command(placeId: "place-1", shopName: null, shopAddress: null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void LatitudeWithoutLongitude_IsRejected()
    {
        var result = Validator.Validate(Command(latitude: 10, longitude: null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Latitude and longitude must be provided together");
    }

    [Fact]
    public void LatitudeOutOfRange_IsRejected()
    {
        var result = Validator.Validate(Command(latitude: 91, longitude: 0));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Latitude must be between -90 and 90");
    }

    [Fact]
    public void LongitudeOutOfRange_IsRejected()
    {
        var result = Validator.Validate(Command(latitude: 0, longitude: 181));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Longitude must be between -180 and 180");
    }

    [Fact]
    public void CaptionOverTwoThousandChars_IsRejected()
    {
        var result = Validator.Validate(Command(caption: new string('a', 2001)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Caption is too long, maximum length is 2000 characters");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void RateOutOfOneToFive_IsRejected(int rate)
    {
        var result = Validator.Validate(Command(rate: rate));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Rate must be between 1 and 5");
    }

    [Fact]
    public void ImageWithNoFileName_IsRejected()
    {
        var result = Validator.Validate(Command(images: [(new MemoryStream(), "")]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "FileName is required");
    }

    [Fact]
    public void NeitherShopNameNorAddress_ProducesTwoErrors()
    {
        var result = Validator.Validate(Command(shopName: "", shopAddress: ""));

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count(e => e.ErrorMessage is "ShopName is required" or "ShopAddress is required"));
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(Command());

        Assert.True(result.IsValid);
    }
}
