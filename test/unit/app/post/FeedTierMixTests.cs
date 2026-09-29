using app;

namespace unit;

public class FeedTierMixTests
{
    private static readonly FeedTier[] Weighted =
    {
        FeedTier.Following,
        FeedTier.Nearby,
        FeedTier.Affinity,
        FeedTier.Trusted
    };

    private static IReadOnlyDictionary<FeedTier, int> Allocate(int pageSize, params FeedTier[] available) =>
        FeedTierMix.Allocate(available.ToHashSet(), pageSize);

    [Fact]
    public void LocationAndFollowing_UsesTheFullRow()
    {
        var quota = Allocate(20, Weighted);

        Assert.Equal(4, quota[FeedTier.Following]);
        Assert.Equal(10, quota[FeedTier.Nearby]);
        Assert.Equal(3, quota[FeedTier.Affinity]);
        Assert.Equal(3, quota[FeedTier.Trusted]);
    }

    [Fact]
    public void NoLocation_ShiftsWeightToAffinityAndTrusted()
    {
        var quota = Allocate(20, FeedTier.Following, FeedTier.Affinity, FeedTier.Trusted);

        Assert.Equal(4, quota[FeedTier.Following]);
        Assert.Equal(0, quota[FeedTier.Nearby]);
        Assert.Equal(10, quota[FeedTier.Affinity]);
        Assert.Equal(6, quota[FeedTier.Trusted]);
    }

    [Fact]
    public void NoFollowing_LeavesLocalityDominant()
    {
        var quota = Allocate(20, FeedTier.Nearby, FeedTier.Affinity, FeedTier.Trusted);

        Assert.Equal(0, quota[FeedTier.Following]);
        Assert.Equal(11, quota[FeedTier.Nearby]);
        Assert.Equal(6, quota[FeedTier.Affinity]);
        Assert.Equal(3, quota[FeedTier.Trusted]);
    }

    [Fact]
    public void NeitherLocationNorFollowing_SplitsAffinityAndTrusted()
    {
        var quota = Allocate(20, FeedTier.Affinity, FeedTier.Trusted);

        Assert.Equal(10, quota[FeedTier.Affinity]);
        Assert.Equal(10, quota[FeedTier.Trusted]);
    }

    // The four rows do not name every combination. A viewer who follows accounts but has never
    // reacted has no affinity source, and the surviving weights have to renormalise themselves
    // rather than leaving the page four items short.
    [Fact]
    public void UnlistedCombination_RenormalisesOverTheSurvivors()
    {
        var quota = Allocate(20, FeedTier.Following, FeedTier.Trusted);

        Assert.Equal(0, quota[FeedTier.Affinity]);
        Assert.Equal(8, quota[FeedTier.Following]);
        Assert.Equal(12, quota[FeedTier.Trusted]);
    }

    // The reason allocation is largest-remainder rather than truncation: at a page size of 3,
    // flooring every share independently returns one item and silently loses two.
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(20)]
    [InlineData(100)]
    public void QuotasAlwaysSumToPageSize(int pageSize)
    {
        FeedTier[][] rows =
        {
            Weighted,
            new[] { FeedTier.Following, FeedTier.Affinity, FeedTier.Trusted },
            new[] { FeedTier.Nearby, FeedTier.Affinity, FeedTier.Trusted },
            new[] { FeedTier.Affinity, FeedTier.Trusted }
        };

        foreach(var row in rows)
        {
            Assert.Equal(pageSize, Allocate(pageSize, row).Values.Sum());
        }
    }

    // Nothing available at all is the brand-new account, and it must allocate nothing rather
    // than divide by a zero weight total. The handler falls through to the backstop.
    [Fact]
    public void NothingAvailable_AllocatesNothing()
    {
        Assert.Equal(0, Allocate(20).Values.Sum());
    }
}
