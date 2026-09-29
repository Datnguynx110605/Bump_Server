using app;

namespace unit;

public class GetRelatedPostValidatorTests
{
    private static readonly GetRelatedPostValidator Validator = new();
    private static readonly CursorPageRequest Page = new(null, 20);

    private static GetRelatedPostQuery Query(double? latitude, double? longitude) =>
        new(FeedMode.Refresh, latitude, longitude, Page);

    // No location is the permission-denied path, not an error: the mix simply resolves to one
    // of its no-location rows.
    [Fact]
    public void NeitherCoordinate_IsAccepted()
    {
        Assert.True(Validator.Validate(Query(null, null)).IsValid);
    }

    [Fact]
    public void BothCoordinates_AreAccepted()
    {
        Assert.True(Validator.Validate(Query(10.762622, 106.660172)).IsValid);
    }

    // One coordinate alone describes no place. Accepting it as "no location" would hide a
    // client bug behind a feed that quietly stops being local.
    [Theory]
    [InlineData(10.76, null)]
    [InlineData(null, 106.66)]
    public void OneCoordinateAlone_IsRejected(double? latitude, double? longitude)
    {
        var result = Validator.Validate(Query(latitude, longitude));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Latitude and longitude must be supplied together");
    }

    [Theory]
    [InlineData(-91d, 0d)]
    [InlineData(91d, 0d)]
    public void LatitudeOutOfRange_IsRejected(double latitude, double longitude)
    {
        var result = Validator.Validate(Query(latitude, longitude));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Latitude must be between -90 and 90");
    }

    [Theory]
    [InlineData(0d, -181d)]
    [InlineData(0d, 181d)]
    public void LongitudeOutOfRange_IsRejected(double latitude, double longitude)
    {
        var result = Validator.Validate(Query(latitude, longitude));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Longitude must be between -180 and 180");
    }

    [Theory]
    [InlineData(90d, 180d)]
    [InlineData(-90d, -180d)]
    public void CoordinatesAtTheLimit_AreAccepted(double latitude, double longitude)
    {
        Assert.True(Validator.Validate(Query(latitude, longitude)).IsValid);
    }
}
