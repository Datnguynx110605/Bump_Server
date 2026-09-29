using app;

namespace unit;

// The cursor is the entire feed state, so every transition below is the difference between a
// viewer seeing the rest of their feed and silently losing a band of it.
public class FeedCursorTests
{
    private static string Mark(int day) =>
        CursorCodec.Encode(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(day), Guid.Empty);

    private static TierCursor Tier(
        string? top = null,
        string? floor = null,
        string? aPos = null,
        string? bPos = null,
        FeedPhase phase = FeedPhase.A,
        bool exhausted = false,
        long? cellLat = null,
        long? cellLng = null) =>
        new(top, floor, aPos, bPos, phase, exhausted, cellLat, cellLng);

    // --- Phase A exit -------------------------------------------------------------------

    // Merging before deciding is what keeps Floor meaningful on the terminal exit too. A
    // terminal tier whose Floor was left null would give the next Refresh an unbounded walk
    // and re-serve the whole corpus.
    [Fact]
    public void PhaseAExit_MergesBeforeDeciding_OnTheTerminalPath()
    {
        var next = TierCursor.Empty.Advance(Mark(9), Mark(5), paginatorHasMore: false);

        Assert.Equal(Mark(9), next.Top);
        Assert.Equal(Mark(9), next.Floor);
        Assert.Null(next.APos);
        Assert.True(next.Exhausted);
        Assert.Equal(FeedPhase.A, next.Phase);
        Assert.True(next.IsTerminal);
    }

    [Fact]
    public void PhaseAExit_MergesBeforeDeciding_OnThePhaseBPath()
    {
        var start = Tier(top: Mark(5), floor: Mark(1), bPos: Mark(0));

        var next = start.Advance(Mark(9), Mark(2), paginatorHasMore: false);

        Assert.Equal(Mark(9), next.Top);
        Assert.Equal(Mark(9), next.Floor);
        Assert.Null(next.APos);
        Assert.Equal(FeedPhase.B, next.Phase);
        Assert.False(next.Exhausted);
        Assert.Equal(Mark(0), next.BPos);
    }

    // Below(null) omits its clause and therefore means "start from the newest". Without the
    // Exhausted flag, an unbounded Phase A that finished would drop into Phase B with a null
    // BPos and replay every post it had just served.
    [Fact]
    public void FirstSessionPhaseACompletion_IsTerminal_NotPhaseBWithNullBPos()
    {
        var next = TierCursor.Empty.Advance(Mark(9), Mark(1), paginatorHasMore: false);

        Assert.NotEqual(FeedPhase.B, next.Phase);
        Assert.True(next.Exhausted);
        Assert.Null(next.BPos);
    }

    [Fact]
    public void PhaseAWithMoreRows_StaysInPhaseA()
    {
        var next = Tier(floor: Mark(1)).Advance(Mark(9), Mark(5), paginatorHasMore: true);

        Assert.Equal(FeedPhase.A, next.Phase);
        Assert.Equal(Mark(5), next.APos);
        Assert.Equal(Mark(1), next.Floor);
        Assert.False(next.Exhausted);
    }

    [Fact]
    public void PhaseBExhaustion_IsTerminal()
    {
        var start = Tier(top: Mark(9), floor: Mark(9), bPos: Mark(3), phase: FeedPhase.B);

        var next = start.Advance(null, Mark(1), paginatorHasMore: false);

        Assert.Equal(Mark(1), next.BPos);
        Assert.True(next.Exhausted);
    }

    // --- Refresh ------------------------------------------------------------------------

    // Advancing Floor mid-Phase-A would orphan the unwalked gap between Floor and APos for
    // good: the new walk starts above it and Phase B starts below it.
    [Fact]
    public void RefreshMidPhaseA_LeavesFloorAlone()
    {
        var start = Tier(top: Mark(9), floor: Mark(1), aPos: Mark(5), bPos: Mark(0));

        var next = start.OnRefresh();

        Assert.Equal(Mark(1), next.Floor);
        Assert.Null(next.APos);
        Assert.Equal(Mark(9), next.Top);
    }

    // Assigned outright rather than trusting the Phase A exit to have already put Top there.
    // Floor must be the OLD Top: set to the new one, Phase A would have nothing to walk and
    // every new post would be skipped.
    [Fact]
    public void RefreshInPhaseB_AssignsFloorFromTopOld()
    {
        var start = Tier(top: Mark(9), floor: Mark(2), bPos: Mark(3), phase: FeedPhase.B);

        var next = start.OnRefresh();

        Assert.Equal(Mark(9), next.Floor);
        Assert.Equal(FeedPhase.A, next.Phase);
        Assert.Null(next.APos);
        Assert.Equal(Mark(3), next.BPos);
    }

    [Fact]
    public void FirstSessionPromotion_TurnsTheUpperIntervalIntoTheLowerOne()
    {
        var start = Tier(top: Mark(9), aPos: Mark(5));

        var next = start.OnRefresh();

        Assert.Equal(Mark(9), next.Floor);
        Assert.Equal(Mark(5), next.BPos);
        Assert.Null(next.APos);
    }

    // Promoting a null APos would put null into BPos, which Phase B reads as "start from the
    // newest" — the same replay the Exhausted flag exists to prevent.
    [Fact]
    public void FirstSessionPromotion_IsSkippedWhenNothingWasEverServed()
    {
        var next = TierCursor.Empty.OnRefresh();

        Assert.Null(next.Floor);
        Assert.Null(next.BPos);
    }

    // A terminal tier has no history left below it, so a Refresh may only walk what is new.
    // Dropping it back into Phase B would re-serve the corpus it already finished.
    [Fact]
    public void TerminalSurvivesRefresh_AndReturnsToTerminal_NeverPhaseB()
    {
        var terminal = Tier(top: Mark(9), floor: Mark(9), phase: FeedPhase.A, exhausted: true);

        var refreshed = terminal.OnRefresh();

        Assert.True(refreshed.Exhausted);
        Assert.Equal(FeedPhase.A, refreshed.Phase);
        Assert.Equal(Mark(9), refreshed.Floor);

        var next = refreshed.Advance(Mark(12), Mark(10), paginatorHasMore: false);

        Assert.NotEqual(FeedPhase.B, next.Phase);
        Assert.True(next.IsTerminal);
        Assert.Equal(Mark(12), next.Top);
    }

    // --- Encoding -----------------------------------------------------------------------

    [Fact]
    public void EveryFieldRoundTrips()
    {
        var cursor = FeedCursor.Fresh()
            .With(FeedTier.Following, Tier(Mark(9), Mark(4), Mark(6), Mark(2)))
            .With(FeedTier.Nearby, Tier(Mark(8), phase: FeedPhase.B, exhausted: true, cellLat: 10_770_000, cellLng: 106_700_000))
            .With(FeedTier.Trusted, Tier(bPos: Mark(1), phase: FeedPhase.B));

        var decoded = FeedCursor.Decode(cursor.Encode());

        Assert.NotNull(decoded);
        Assert.Equal(Mark(9), decoded![FeedTier.Following].Top);
        Assert.Equal(Mark(4), decoded[FeedTier.Following].Floor);
        Assert.Equal(Mark(6), decoded[FeedTier.Following].APos);
        Assert.Equal(Mark(2), decoded[FeedTier.Following].BPos);
        Assert.True(decoded[FeedTier.Nearby].Exhausted);
        Assert.Equal(FeedPhase.B, decoded[FeedTier.Nearby].Phase);
        Assert.Equal(10_770_000, decoded[FeedTier.Nearby].CellLat);
        Assert.Equal(106_700_000, decoded[FeedTier.Nearby].CellLng);
        Assert.Equal(FeedPhase.B, decoded[FeedTier.Trusted].Phase);
        Assert.Equal(Mark(1), decoded[FeedTier.Trusted].BPos);
        Assert.Equal(FeedPhase.A, decoded[FeedTier.Affinity].Phase);
        Assert.Null(decoded[FeedTier.Affinity].Top);
    }

    // The origin survives as micro-degrees, which is also why '.' stays usable as the tier
    // delimiter: a decimal point in the token would split a coordinate in half.
    [Fact]
    public void OriginRoundTrips()
    {
        var cursor = FeedCursor.Fresh() with { Lat = 10.762622, Lng = 106.660172 };

        var decoded = FeedCursor.Decode(cursor.Encode());

        Assert.NotNull(decoded);
        Assert.Equal(10.762622, decoded!.Lat!.Value, 5);
        Assert.Equal(106.660172, decoded.Lng!.Value, 5);
    }

    [Fact]
    public void AbsentOriginRoundTripsAsNull()
    {
        var decoded = FeedCursor.Decode(FeedCursor.Fresh().Encode());

        Assert.NotNull(decoded);
        Assert.Null(decoded!.Lat);
        Assert.Null(decoded.Lng);
    }

    // Live clients hold v1 cursors from the previous feed — a bare base64url mark carrying
    // none of this structure. Degrading to a fresh Refresh is what keeps the rollout from
    // being a 500 on every request until those cursors expire.
    [Fact]
    public void LegacyCursorDegradesToFreshRefresh()
    {
        string v1 = CursorCodec.Encode(DateTime.UtcNow, Guid.NewGuid());

        Assert.Null(FeedCursor.Decode(v1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-cursor")]
    [InlineData("v2.a.b")]
    [InlineData("v3....")]
    public void UnparseableCursorDegradesToFreshRefresh(string? cursor)
    {
        Assert.Null(FeedCursor.Decode(cursor));
    }

    // --- Origin and the cell-scoped nearby reset ------------------------------------------

    private static FeedCursor Walked() =>
        FeedCursor.Fresh()
            .With(FeedTier.Nearby, Tier(Mark(9), Mark(4), Mark(6), Mark(2), cellLat: 10_760_000, cellLng: 106_660_000))
            .With(FeedTier.Following, Tier(Mark(9), Mark(4), Mark(6), Mark(2)))
            .With(FeedTier.Trusted, Tier(Mark(8), Mark(3), bPos: Mark(1), phase: FeedPhase.B));

    // Nearby's marks index one shop set. Carried into a different cell they would have the
    // server assume the client already holds a band of the NEW area, and every post in it
    // would be skipped in silence.
    [Fact]
    public void MovingToANewCell_ResetsNearby()
    {
        var next = Walked().PinOrigin(10.80, 106.66, 10_800_000, 106_660_000);

        Assert.Equal(TierCursor.Empty with { CellLat = 10_800_000, CellLng = 106_660_000 }, next[FeedTier.Nearby]);
    }

    [Fact]
    public void GainingALocation_ResetsNearby()
    {
        var withoutLocation = FeedCursor.Fresh()
            .With(FeedTier.Nearby, Tier(Mark(9), Mark(4), Mark(6), Mark(2)));

        var next = withoutLocation.PinOrigin(10.76, 106.66, 10_760_000, 106_660_000);

        Assert.Equal(TierCursor.Empty with { CellLat = 10_760_000, CellLng = 106_660_000 }, next[FeedTier.Nearby]);
    }

    // A Refresh with no coordinates clears the pin, and the marks have to go with it — leaving
    // them would let a later Refresh in the same cell resume a walk the viewer never finished
    // against a shop list that has since been re-resolved.
    [Fact]
    public void LosingLocation_ClearsThePinAndResetsNearby()
    {
        var next = Walked().PinOrigin(null, null, null, null);

        Assert.Null(next.Lat);
        Assert.Null(next.Lng);
        Assert.Equal(TierCursor.Empty, next[FeedTier.Nearby]);
    }

    [Fact]
    public void StayingInTheSameCell_LeavesNearbyAlone()
    {
        var before = Walked();

        var next = before.PinOrigin(10.7638, 106.6641, 10_760_000, 106_660_000);

        Assert.Equal(before[FeedTier.Nearby], next[FeedTier.Nearby]);
        Assert.Equal(10.7638, next.Lat!.Value, 5);
    }

    // The reset is surgical. A viewer crossing a cell boundary must not lose their place in
    // the tiers that have nothing to do with where they are standing.
    [Fact]
    public void ResettingNearby_LeavesEveryOtherTierUntouched()
    {
        var before = Walked();

        var next = before.PinOrigin(10.80, 106.66, 10_800_000, 106_660_000);

        Assert.Equal(before[FeedTier.Following], next[FeedTier.Following]);
        Assert.Equal(before[FeedTier.Affinity], next[FeedTier.Affinity]);
        Assert.Equal(before[FeedTier.Trusted], next[FeedTier.Trusted]);
        Assert.Equal(before[FeedTier.Global], next[FeedTier.Global]);
    }

    [Fact]
    public void FreshCursorHasEveryTierAtTheStart()
    {
        var cursor = FeedCursor.Fresh();

        foreach(var tier in Enum.GetValues<FeedTier>())
        {
            Assert.Equal(TierCursor.Empty, cursor[tier]);
            Assert.False(cursor[tier].IsTerminal);
        }
    }
}
