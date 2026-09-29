namespace app;

// The blend. Which row applies is decided by which tiers returned source ids, never by which
// request parameters were set: a viewer who follows two hundred accounts that have never
// posted has no following source, and falls to the next row on its own.
//
// Availability is not productivity. A tier can have sources and still yield nothing on this
// walk — a followed account with no posts inside the phase's range. The row is chosen once,
// before the fetch; a tier that then comes back short is handled by in-phase re-fetch and
// shortfall refill, never by re-deriving the row afterwards.
internal static class FeedTierMix
{
    private readonly record struct Weights(int Following, int Nearby, int Affinity, int Trusted);

    // Affinity holds a floor in the full row rather than sitting at zero: engagement history
    // is the strongest personalisation signal in the system, and a signal that only appears
    // when something else is missing is dead weight in the happy path.
    //
    // The no-following row leans hardest on locality, because a viewer who follows nobody is a
    // pure discovery user and is also unlikely to have much of an affinity list.
    private static readonly Weights LocationAndFollowing = new(Following: 20, Nearby: 50, Affinity: 15, Trusted: 15);
    private static readonly Weights FollowingOnly = new(Following: 20, Nearby: 0, Affinity: 50, Trusted: 30);
    private static readonly Weights LocationOnly = new(Following: 0, Nearby: 55, Affinity: 30, Trusted: 15);
    private static readonly Weights Neither = new(Following: 0, Nearby: 0, Affinity: 50, Trusted: 50);

    public static IReadOnlyDictionary<FeedTier, int> Allocate(
        IReadOnlySet<FeedTier> available,
        int pageSize)
    {
        var row = (available.Contains(FeedTier.Following), available.Contains(FeedTier.Nearby)) switch
        {
            (true, true) => LocationAndFollowing,
            (true, false) => FollowingOnly,
            (false, true) => LocationOnly,
            (false, false) => Neither
        };

        // A tier with no sources contributes zero and the survivors renormalise themselves
        // through the largest-remainder pass below. That is what covers every combination the
        // four rows do not name — no affinity yet, or no trusted accounts yet.
        var weights = new Dictionary<FeedTier, int>
        {
            [FeedTier.Nearby] = available.Contains(FeedTier.Nearby) ? row.Nearby : 0,
            [FeedTier.Following] = available.Contains(FeedTier.Following) ? row.Following : 0,
            [FeedTier.Affinity] = available.Contains(FeedTier.Affinity) ? row.Affinity : 0,
            [FeedTier.Trusted] = available.Contains(FeedTier.Trusted) ? row.Trusted : 0
        };

        return LargestRemainder(weights, pageSize);
    }

    // Floor then distribute the remainder, rather than (int)(pageSize * share). Plain
    // truncation loses a row per tier on every small page — a pageSize of 3 across four tiers
    // would return two items — and the shortfall would be invisible, since every tier would
    // look as though it had simply come back short.
    private static IReadOnlyDictionary<FeedTier, int> LargestRemainder(
        Dictionary<FeedTier, int> weights,
        int pageSize)
    {
        int total = weights.Values.Sum();

        if(total == 0)
            return weights;

        var exact = weights.ToDictionary(x => x.Key, x => (double)x.Value * pageSize / total);
        var quota = exact.ToDictionary(x => x.Key, x => (int)Math.Floor(x.Value));

        int remaining = pageSize - quota.Values.Sum();

        // Ordered by fractional part and then by tier, so a tie resolves the same way every
        // time. A nondeterministic allocation would make page composition untestable.
        var winners = exact
            .Where(x => weights[x.Key] > 0)
            .OrderByDescending(x => x.Value - Math.Floor(x.Value))
            .ThenBy(x => x.Key)
            .Take(remaining)
            .Select(x => x.Key);

        foreach(var tier in winners)
        {
            quota[tier]++;
        }

        return quota;
    }
}
