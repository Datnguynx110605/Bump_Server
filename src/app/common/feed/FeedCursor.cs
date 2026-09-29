using System.Globalization;
using domain;
using MongoDB.Driver;

namespace app;

// Which of a tier's two walks is running. Phase A covers the band newer than the previous
// session; Phase B covers everything below what has ever been served.
internal enum FeedPhase
{
    A,
    B
}

// One tier's position in the feed, and the whole of it — nothing per-viewer is stored
// server-side, so every mark the next request needs has to survive in the token.
//
// Top and APos bracket the current Phase A walk; BPos and Floor bracket the region served in
// past sessions. Phase A closes the gap between the two.
//
// The cell the nearby marks were built against is carried as two scaled integers rather than
// a formatted coordinate pair, for the same reason as the origin below: a decimal point would
// split the token on its own delimiter. Keeping it structured also means no string format has
// to be kept in step between this and the Redis key the resolver builds.
//
// Exhausted is not derivable from the marks. Below(null) omits its clause and therefore means
// "start from the newest", so a null BPos cannot distinguish "not started" from "reached the
// bottom": without the flag, a tier that finished an unbounded Phase A would drop into Phase B
// with a null BPos and replay its entire corpus from the top.
internal sealed record TierCursor(
    string? Top,
    string? Floor,
    string? APos,
    string? BPos,
    FeedPhase Phase,
    bool Exhausted,
    long? CellLat,
    long? CellLng)
{
    private const int FieldCount = 8;

    public static readonly TierCursor Empty =
        new(null, null, null, null, FeedPhase.A, false, null, null);

    // The mark the paginator resumes from. Passed as CursorPageRequest.Cursor rather than
    // folded into the filter, because CacheKey.List builds the Phase B page key from it and a
    // position hidden in the filter would collide every page of the walk onto one key.
    public string? Position => Phase == FeedPhase.A ? APos : BPos;

    // Phase A's lower bound. Phase B has none — it walks to the bottom of the tier.
    public FilterDefinition<T>? LowerBound<T>() where T : BaseEntity =>
        Phase == FeedPhase.A ? Above<T>(Floor) : null;

    // The inverse of the keyset predicate CursorPaginator builds, with the same Id tie-break,
    // so one mark bounds the walk identically from either direction.
    private static FilterDefinition<T>? Above<T>(string? mark) where T : BaseEntity
    {
        var decoded = CursorCodec.Decode(mark);

        if(decoded is null)
            return null;

        return Builders<T>.Filter.Or(
            Builders<T>.Filter.Gt(x => x.CreatedAt, decoded.Value.CreatedAt),
            Builders<T>.Filter.And(
                Builders<T>.Filter.Eq(x => x.CreatedAt, decoded.Value.CreatedAt),
                Builders<T>.Filter.Gt(x => x.Id, decoded.Value.Id)));
    }

    // Marks move by CONSUMED items, never by fetched ones: the tier fetched its whole
    // over-fetch budget, the allocator took a contiguous prefix of that, and everything past
    // the prefix has to stay reachable on the next request.
    public TierCursor Advance(string? firstConsumed, string? lastConsumed, bool paginatorHasMore)
    {
        // Top is established on the first page of a Refresh's Phase A walk and then held: it
        // is the top of the served interval and becomes the next Refresh's Floor. A Phase A
        // that returns nothing leaves it alone.
        var top = Phase == FeedPhase.A && APos is null && firstConsumed is not null
            ? firstConsumed
            : Top;

        if(Phase == FeedPhase.B)
            return this with
            {
                Top = top,
                BPos = lastConsumed ?? BPos,
                Exhausted = Exhausted || !paginatorHasMore
            };

        if(paginatorHasMore)
            return this with { Top = top, APos = lastConsumed ?? APos };

        // Phase A is over. Merge the two intervals FIRST and unconditionally: Floor has to be
        // meaningful on the terminal exit too, because the next Refresh uses it as the lower
        // bound of the new walk and a null there would re-walk the whole corpus.
        bool hadNoLowerBound = Floor is null;

        var merged = this with { Top = top, Floor = top, APos = null };

        // A walk that began with no lower bound ran to the bottom of the tier, so there is
        // nothing left below it for Phase B to cover.
        return hadNoLowerBound || Exhausted
            ? merged with { Exhausted = true }
            : merged with { Phase = FeedPhase.B };
    }

    // Restart the Phase A walk from the newest data. What this must never do is advance Floor
    // to the NEW Top: Phase A would then have nothing between Floor and Top to walk, and every
    // new post would be skipped.
    public TierCursor OnRefresh()
    {
        // Phase B or terminal: the served set is one interval, so the new walk runs down to
        // Top_old. Assigned outright rather than trusting the Phase A exit to have left Floor
        // holding that value already.
        if(Phase == FeedPhase.B || Exhausted)
            return this with { Floor = Top, APos = null, Phase = FeedPhase.A };

        // First-ever session: there is no lower interval, so the upper one simply becomes it.
        // Guarded on APos, because promoting a null into BPos would mean "Phase B starts from
        // the newest" — the replay Exhausted exists to prevent.
        if(Floor is null && BPos is null)
            return APos is null
                ? this
                : this with { Floor = Top, BPos = APos, APos = null };

        // Mid-Phase-A: two intervals with an unwalked gap between them. Advancing Floor here
        // would orphan that gap permanently, so it stays put and the new walk re-serves
        // [APos, Top]. The client dedupes by post id on append.
        return this with { APos = null };
    }

    // A tier is done when it has reached the bottom of its corpus and any Phase A walk it is
    // currently running has also finished.
    public bool IsTerminal => Exhausted && Phase == FeedPhase.A && APos is null;

    public string Encode() =>
        string.Join(
            '~',
            Top ?? string.Empty,
            Floor ?? string.Empty,
            APos ?? string.Empty,
            BPos ?? string.Empty,
            Phase == FeedPhase.B ? "B" : "A",
            Exhausted ? "1" : "0",
            Scalar(CellLat),
            Scalar(CellLng));

    public static TierCursor Decode(string value)
    {
        var fields = value.Split('~');

        if(fields.Length != FieldCount)
            throw new FormatException("Tier cursor must carry eight fields");

        return new TierCursor(
            OrNull(fields[0]),
            OrNull(fields[1]),
            OrNull(fields[2]),
            OrNull(fields[3]),
            fields[4] == "B" ? FeedPhase.B : FeedPhase.A,
            fields[5] == "1",
            Scalar(fields[6]),
            Scalar(fields[7]));
    }

    private static string? OrNull(string value) => value.Length == 0 ? null : value;

    private static string Scalar(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static long? Scalar(string value) =>
        value.Length == 0 ? null : long.Parse(value, CultureInfo.InvariantCulture);
}

// The entire feed state. Nothing per-viewer is held server-side, which is why every tier's
// marks, its phase and the session's pinned origin all have to round-trip through the token.
//
// Encoded as version-tagged fields joined by '.', with each tier's own fields joined by '~'.
// Both delimiters are RFC 3986 unreserved and neither appears in the base64url alphabet the
// marks use, so the token needs no percent-encoding in a query string. The origin is carried
// as micro-degrees for the same reason: a decimal point would collide with the '.' delimiter,
// and an integer also sidesteps culture-dependent formatting entirely.
internal sealed record FeedCursor(double? Lat, double? Lng, TierCursor[] Tiers)
{
    private const string Version = "v2";
    private const double CoordinateScale = 1_000_000d;

    private static readonly FeedTier[] Order = Enum.GetValues<FeedTier>();

    public TierCursor this[FeedTier tier] => Tiers[(int)tier];

    public static FeedCursor Fresh() =>
        new(null, null, Order.Select(_ => TierCursor.Empty).ToArray());

    public FeedCursor With(FeedTier tier, TierCursor cursor)
    {
        var tiers = (TierCursor[])Tiers.Clone();
        tiers[(int)tier] = cursor;

        return this with { Tiers = tiers };
    }

    // Pins the session origin, and resets the nearby tier whenever the cell it walks has
    // changed underneath it.
    //
    // Nearby's marks are time positions over ONE shop set — the shops around that cell — and
    // mean nothing against a different one. Carrying them across a move would have the server
    // assume the client already holds [BPos, Floor] for the new area, so every new-area post
    // inside that range would be skipped in silence.
    //
    // One comparison covers all three cases: the cell changed, nearby became available, or
    // nearby became unavailable and the pin was cleared. The reset re-serves nearby posts the
    // client may still hold, which it dedupes on append — the alternative is losing the new
    // neighbourhood's back catalogue outright. No other tier is touched.
    public FeedCursor PinOrigin(double? latitude, double? longitude, long? cellLat, long? cellLng)
    {
        var nearby = this[FeedTier.Nearby];

        var next = nearby.CellLat != cellLat || nearby.CellLng != cellLng
            ? With(FeedTier.Nearby, TierCursor.Empty with { CellLat = cellLat, CellLng = cellLng })
            : this;

        return next with { Lat = latitude, Lng = longitude };
    }

    public string Encode()
    {
        var parts = new List<string>(Order.Length + 3)
        {
            Version,
            Scale(Lat),
            Scale(Lng)
        };

        parts.AddRange(Tiers.Select(x => x.Encode()));

        return string.Join('.', parts);
    }

    // A cursor that will not parse degrades to a fresh Refresh rather than throwing. Live
    // clients hold v1 cursors from the previous feed, which are a bare base64url mark carrying
    // none of this structure and would otherwise be a 500 on every request until they expire.
    // Same contract as CursorCodec.Decode.
    public static FeedCursor? Decode(string? cursor)
    {
        if(string.IsNullOrEmpty(cursor))
            return null;

        var parts = cursor.Split('.');

        if(parts.Length != Order.Length + 3 || parts[0] != Version)
            return null;

        try
        {
            var tiers = new TierCursor[Order.Length];

            for(int i = 0; i < Order.Length; i++)
                tiers[i] = TierCursor.Decode(parts[i + 3]);

            return new FeedCursor(Unscale(parts[1]), Unscale(parts[2]), tiers);
        }
        catch(FormatException)
        {
            return null;
        }
    }

    private static string Scale(double? coordinate) =>
        coordinate is null
            ? string.Empty
            : ((long)Math.Round(coordinate.Value * CoordinateScale)).ToString(CultureInfo.InvariantCulture);

    private static double? Unscale(string value) =>
        value.Length == 0
            ? null
            : long.Parse(value, CultureInfo.InvariantCulture) / CoordinateScale;
}
