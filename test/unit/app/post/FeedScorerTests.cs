using app;
using domain;

namespace unit;

// The scorer reorders an already-assembled page. Each test moves exactly one term and asserts
// the order follows it, so a change to one weight cannot quietly invert another's effect.
public class FeedScorerTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private static PostSummaryDto Post(
        Guid? shopId = null,
        int rate = 3,
        int reactions = 0,
        int comments = 0,
        double ageHours = 0) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            shopId ?? Guid.NewGuid(),
            new List<ImageDto>(),
            null,
            Amount.Create(rate).Value!,
            Amount.Create(reactions).Value!,
            Amount.Create(comments).Value!,
            Now.AddHours(-ageHours));

    private static List<PostSummaryDto> Rank(
        IReadOnlyDictionary<Guid, double>? distances,
        params (PostSummaryDto Post, FeedTier Tier)[] page) =>
        FeedScorer.Rank(page, distances ?? new Dictionary<Guid, double>(), Now);

    [Fact]
    public void HigherRate_RanksAbove()
    {
        var low = Post(rate: 1);
        var high = Post(rate: 5);

        var ranked = Rank(null, (low, FeedTier.Following), (high, FeedTier.Following));

        Assert.Equal(high.Id, ranked[0].Id);
    }

    [Fact]
    public void MoreEngagement_RanksAbove()
    {
        var quiet = Post(reactions: 0, comments: 0);
        var busy = Post(reactions: 40, comments: 10);

        var ranked = Rank(null, (quiet, FeedTier.Following), (busy, FeedTier.Following));

        Assert.Equal(busy.Id, ranked[0].Id);
    }

    // Comments weigh more than reactions: writing something takes more than tapping.
    [Fact]
    public void CommentsOutweighReactionsOneForOne()
    {
        var reacted = Post(reactions: 10);
        var discussed = Post(comments: 10);

        var ranked = Rank(null, (reacted, FeedTier.Following), (discussed, FeedTier.Following));

        Assert.Equal(discussed.Id, ranked[0].Id);
    }

    [Fact]
    public void NewerPost_RanksAbove()
    {
        var old = Post(ageHours: 72);
        var fresh = Post(ageHours: 1);

        var ranked = Rank(null, (old, FeedTier.Following), (fresh, FeedTier.Following));

        Assert.Equal(fresh.Id, ranked[0].Id);
    }

    [Fact]
    public void CloserShop_RanksAbove()
    {
        var far = Post();
        var near = Post();

        var distances = new Dictionary<Guid, double>
        {
            [far.ShopId] = 9_500,
            [near.ShopId] = 50
        };

        var ranked = Rank(distances, (far, FeedTier.Nearby), (near, FeedTier.Nearby));

        Assert.Equal(near.Id, ranked[0].Id);
    }

    // Only the nearby tier carries a shop the viewer is standing near. A post from any other
    // tier must not collect proximity credit it never earned, which is why an unknown distance
    // scores zero rather than defaulting to "close".
    [Fact]
    public void UnknownDistance_EarnsNoProximityCredit()
    {
        var known = Post();
        var unknown = Post();

        var distances = new Dictionary<Guid, double> { [known.ShopId] = 0 };

        var ranked = Rank(distances, (unknown, FeedTier.Nearby), (known, FeedTier.Nearby));

        Assert.Equal(known.Id, ranked[0].Id);
    }

    // The smallest term in the formula: a tie-breaker between otherwise comparable posts, not
    // a way to reintroduce a quota the mix has already decided.
    [Fact]
    public void TierBreaksTiesBetweenOtherwiseIdenticalPosts()
    {
        var trusted = Post();
        var following = Post();

        var ranked = Rank(null, (trusted, FeedTier.Trusted), (following, FeedTier.Following));

        Assert.Equal(following.Id, ranked[0].Id);
    }

    // A weak signal must not overturn a strong one: five stars from a trusted account still
    // loses to a one-star post nobody engaged with only if the weights are wrong.
    [Fact]
    public void TierNeverOverturnsRate()
    {
        var goodPostLowTier = Post(rate: 5);
        var badPostHighTier = Post(rate: 1);

        var ranked = Rank(null, (badPostHighTier, FeedTier.Nearby), (goodPostLowTier, FeedTier.Global));

        Assert.Equal(goodPostLowTier.Id, ranked[0].Id);
    }

    // Ranking is a reordering and nothing else. Losing or duplicating a row here would desync
    // the page from the cursors, which were already settled from what the allocator consumed.
    [Fact]
    public void RankingPreservesEveryRowExactlyOnce()
    {
        var page = Enumerable.Range(0, 10)
            .Select(i => (Post(rate: i % 5, ageHours: i), FeedTier.Following))
            .ToArray();

        var ranked = Rank(null, page);

        Assert.Equal(10, ranked.Count);
        Assert.Equal(
            page.Select(x => x.Item1.Id).OrderBy(x => x),
            ranked.Select(x => x.Id).OrderBy(x => x));
    }

    // Stable, so posts sharing a score keep the order their tier's walk returned them in —
    // newest first, the same way the rest of the feed already orders itself.
    [Fact]
    public void EqualScoresKeepTheirWalkOrder()
    {
        var first = Post();
        var second = Post();
        var third = Post();

        var ranked = Rank(
            null,
            (first, FeedTier.Following),
            (second, FeedTier.Following),
            (third, FeedTier.Following));

        Assert.Equal(new[] { first.Id, second.Id, third.Id }, ranked.Select(x => x.Id));
    }

    [Fact]
    public void EmptyPageRanksToEmpty()
    {
        Assert.Empty(Rank(null));
    }
}
