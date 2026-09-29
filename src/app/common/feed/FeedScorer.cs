namespace app;

// Ranks the page that has already been assembled — never the corpus. Each tier is walked in
// index order and the allocator takes a contiguous prefix of it; this reorders those twenty or
// so rows for display and nothing else. That separation is what keeps the cursors honest: the
// score cannot change which posts were consumed, only the order they are shown in.
//
// Every input is already on PostSummaryDto or in the cached nearby shop list, so ranking costs
// no reads at all.
internal static class FeedScorer
{
    // ponytail: hand-set weights, not learned ones. Nothing in this repository logs
    // impressions, so there is no training data to fit them against, and a model with no data
    // is random weights wearing a lab coat. The shape below does not change when impression
    // logging lands — only these five numbers do, which is the whole point of keeping them
    // here as constants rather than folding them into the expression.
    private const double RateWeight = 0.20;
    private const double EngagementWeight = 0.25;
    private const double RecencyWeight = 0.35;
    private const double ProximityWeight = 0.15;
    private const double TierWeight = 0.05;

    private const double MaxRate = 5;

    // Engagement is logarithmic because the difference between two reactions and twenty says
    // far more than the difference between two hundred and two hundred and twenty. The cap is
    // where the curve is treated as saturated.
    private const double EngagementCap = 200;
    private const double CommentMultiplier = 2;

    // A post is worth about half as much twelve hours later. Short, because this is a feed of
    // places people are eating at now.
    private const double RecencyHalfLifeHours = 12;

    // Matches the nearby tier's own radius: a shop at the edge of the search contributes
    // nothing here, one underfoot contributes everything.
    private const double MaxProximityMeters = 10_000;

    public static List<PostSummaryDto> Rank(
        IReadOnlyList<(PostSummaryDto Post, FeedTier Tier)> page,
        IReadOnlyDictionary<Guid, double> shopDistances,
        DateTime now)
    {
        // OrderByDescending is stable, so posts sharing a score keep the order their tier's
        // walk returned them in — which is newest-first. Ties therefore break the way the rest
        // of the feed already orders itself, rather than arbitrarily.
        return page
            .OrderByDescending(x => Score(x.Post, x.Tier, shopDistances, now))
            .Select(x => x.Post)
            .ToList();
    }

    private static double Score(
        PostSummaryDto post,
        FeedTier tier,
        IReadOnlyDictionary<Guid, double> shopDistances,
        DateTime now)
    {
        double engagement = post.ReactionAmount.Value + CommentMultiplier * post.CommentAmount.Value;
        double ageHours = Math.Max((now - post.CreatedAt).TotalHours, 0);

        // Zero rather than one when the distance is unknown: only the nearby tier carries a
        // shop the viewer is near, and a post from any other tier must not collect proximity
        // credit it did not earn.
        double proximity = shopDistances.TryGetValue(post.ShopId, out double meters)
            ? Clamp(1 - meters / MaxProximityMeters)
            : 0;

        return RateWeight * Clamp(post.Rate.Value / MaxRate)
            + EngagementWeight * Clamp(Math.Log(1 + engagement) / Math.Log(1 + EngagementCap))
            + RecencyWeight * Math.Exp(-ageHours / RecencyHalfLifeHours)
            + ProximityWeight * proximity
            + TierWeight * TierBoost(tier);
    }

    // The tiers in the order the product cares about them, and the smallest weight in the
    // formula: it is a tie-breaker between otherwise comparable posts, not a way to
    // reintroduce the quota that the mix has already decided.
    private static double TierBoost(FeedTier tier) => tier switch
    {
        FeedTier.Nearby => 1.0,
        FeedTier.Following => 0.8,
        FeedTier.Affinity => 0.6,
        FeedTier.Trusted => 0.4,
        _ => 0.2
    };

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);
}
