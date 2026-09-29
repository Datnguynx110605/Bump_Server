using app;

namespace unit;

public class NearbyShopResolverTests
{
    // Two viewers resolve the same cell, and therefore the same cached shop list and the same
    // nearby page keys, exactly when their coordinates round to the same hundredth of a degree.
    //
    // Note what this does NOT promise: rounding cells are fixed grid squares, not a radius, so
    // two viewers a few hundred metres apart can still land either side of a boundary and share
    // nothing. Cache sharing is a property of the grid, not of proximity.
    [Fact]
    public void CoordinatesInTheSameCell_ResolveToTheSameCell()
    {
        var a = NearbyShopResolver.Cell(10.7612, 106.6604);
        var b = NearbyShopResolver.Cell(10.7638, 106.6641);

        Assert.Equal(a, b);
    }

    [Fact]
    public void CoordinatesInDifferentCells_ResolveApart()
    {
        var a = NearbyShopResolver.Cell(10.76, 106.66);
        var b = NearbyShopResolver.Cell(10.80, 106.66);

        Assert.NotEqual(a, b);
    }

    // Micro-degrees, the same units the cursor carries its origin in, so a cell can be compared
    // and round-tripped through the token without ever being formatted as a decimal.
    [Fact]
    public void CellIsExpressedInMicroDegrees()
    {
        var (lat, lng) = NearbyShopResolver.Cell(10.762622, 106.660172);

        Assert.Equal(10_760_000, lat);
        Assert.Equal(106_660_000, lng);
    }

    [Fact]
    public void NegativeCoordinatesRoundConsistently()
    {
        var (lat, lng) = NearbyShopResolver.Cell(-33.868, -151.209);

        Assert.Equal(-33_870_000, lat);
        Assert.Equal(-151_210_000, lng);
    }
}
