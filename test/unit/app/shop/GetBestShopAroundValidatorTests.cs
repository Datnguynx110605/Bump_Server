namespace unit;

public class GetBestShopAroundValidatorTests
{
    private static readonly GetBestShopAroundValidator Validator = new();

    [Theory]
    [InlineData(-91)]
    [InlineData(91)]
    public void LatitudeOutOfRange_IsRejected(double latitude)
    {
        var result = Validator.Validate(new GetBestShopAroundQuery(latitude, 0));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Latitude must be between -90 and 90");
    }

    [Theory]
    [InlineData(-181)]
    [InlineData(181)]
    public void LongitudeOutOfRange_IsRejected(double longitude)
    {
        var result = Validator.Validate(new GetBestShopAroundQuery(0, longitude));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Longitude must be between -180 and 180");
    }

    [Fact]
    public void AtLimits_Passes()
    {
        var result = Validator.Validate(new GetBestShopAroundQuery(90, 180));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new GetBestShopAroundQuery(10.5, 106.5));

        Assert.True(result.IsValid);
    }
}
