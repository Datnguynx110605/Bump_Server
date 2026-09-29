using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

// A blended feed, not a cascade. Four weighted tiers are walked in parallel and merged into
// one page, with a fifth — global recent — held back as a backstop so a brand-new account is
// never handed nothing.
//
// Two things carry the design. First, every tier keeps its own keyset walk, so the page stays
// index-sorted and no scoring ever reaches the corpus. Second, each walk runs in one of two
// phases recorded in the cursor: Phase A covers the band newer than the previous session,
// Phase B covers everything below what has ever been served. The request never picks the
// phase — the server does, per tier — which is what stops a multi-page Phase A from being
// abandoned after its first page and orphaning everything below it.
//
// Nearby is absent here by design: it ships with the coordinates it needs, so until then the
// mix resolves to its no-location rows, which is exactly the behaviour a viewer who denies
// location permission gets.
internal sealed class GetRelatedPostHandler
    : IRequestHandler<GetRelatedPostQuery, Result<CursorPageResult<PostFeedItemDto>>>
{
    private const int AffinityLimit = 20;

    // Mongo scans the index once per value in an $in and merge-sorts the results, so the
    // source list has to stay bounded no matter how many accounts a viewer follows.
    private const int MaxSourceAccounts = 300;

    // Same reasoning as MaxSourceAccounts, for the trusted tier's $in.
    private const int MaxTrustedAccounts = 200;

    // ponytail: flat 2x over-fetch so blocked authors can be dropped after the read instead
    // of with a $nin, which would fragment the shared page keys. Raise it if the backstop
    // starts firing often.
    private const int OverFetchMultiplier = 2;
    private const int MaxFetchSize = 50;

    // ponytail: the pin means "I just posted", not "I posted once". Without a bound every
    // Refresh for the rest of time would re-pin a post from days ago, which is a stuck feed
    // header rather than a confirmation that posting worked.
    private static readonly TimeSpan PinMaxAge = TimeSpan.FromHours(6);

    // ponytail: a hard cap, not a retry loop. Tiers parallelise within a round, but a round
    // cannot start until the previous one is filtered, so each is one more sequential hop
    // against the latency budget. Raise the over-fetch before raising this.
    private const int MaxRefetchRounds = 2;

    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetRelatedPostHandler(
        ICurrentIdentityService identityService,
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _identityService = identityService;
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<PostFeedItemDto>>> Handle(
        GetRelatedPostQuery request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        var viewer = identity.Value;
        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);
        int pageSize = request.pageRequest.PageSize;

        // Decoded before anything is dispatched, because it decides the mode, the mode decides
        // which origin applies, and the origin decides what the nearby lookup below asks for.
        //
        // A cursor that will not parse restarts the session rather than failing the request:
        // live clients hold cursors from the previous feed that carry none of this structure.
        var decoded = FeedCursor.Decode(request.pageRequest.Cursor);
        var mode = decoded is null ? FeedMode.Refresh : request.mode;
        var cursor = decoded ?? FeedCursor.Fresh();

        // Refresh takes the client's current position and re-pins it; More uses the pinned one
        // and ignores whatever the client sent. A viewer who moves mid-scroll would otherwise
        // have the nearby walk change corpus underneath its own marks.
        double? latitude = mode == FeedMode.Refresh ? request.latitude : cursor.Lat;
        double? longitude = mode == FeedMode.Refresh ? request.longitude : cursor.Lng;

        if(mode == FeedMode.Refresh)
            cursor = Refresh(cursor, latitude, longitude);

        // Hop 1. These read different collections and share no state, so they overlap rather
        // than costing five round trips back to back.
        var hiddenTask = BlockedAccountSet.ResolveAsync(_redisService, _dbContext, viewer, cancellationToken);
        var reactedTask = ReactedPostSet.ResolveAsync(_redisService, _dbContext, viewer, cancellationToken);
        var followingTask = GetFollowingIdsAsync(viewer, cancellationToken);
        var affinityTask = GetAffinityIdsAsync(postCollection, viewer, cancellationToken);
        var trustedTask = GetTrustedIdsAsync(cancellationToken);

        var nearbyTask = latitude is not null && longitude is not null
            ? NearbyShopResolver.ResolveAsync(_redisService, _dbContext, latitude.Value, longitude.Value, cancellationToken)
            : Task.FromResult(new List<NearbyShop>());

        // Only ever prepended to a Refresh, so later pages do not pay for a read they would
        // discard. Started here so it overlaps the feed queries instead of trailing them.
        var latestTask = mode == FeedMode.Refresh
            ? GetMyLatestPostAsync(postCollection, viewer, cancellationToken)
            : null;

        var hidden = (await hiddenTask).ToHashSet();
        var reacted = (await reactedTask).ToHashSet();
        var following = await followingTask;
        var affinity = await affinityTask;
        var trusted = await trustedTask;
        var nearby = await nearbyTask;

        var buffers = BuildBuffers(cursor, viewer, following, affinity, trusted, nearby, hidden);

        var available = buffers.Select(x => x.Tier).ToHashSet();
        var quota = FeedTierMix.Allocate(available, pageSize);

        await FillAsync(buffers, quota, postCollection, pageSize, viewer, hidden, reacted, cancellationToken);

        // The page carries each row's tier until ranking, which is the only thing that needs
        // it: the scorer nudges a nearby post above an equally good distant one.
        var page = new List<(PostSummaryDto Post, FeedTier Tier)>(pageSize);
        var seen = new HashSet<Guid>();

        Assemble(buffers, quota, page, seen, pageSize);

        var next = cursor;

        // Only the tiers that actually had sources can keep the stream alive. A tier the
        // viewer has nothing in — no follows, no reactions — never walks and so never becomes
        // terminal, and counting those would leave HasMore true forever: the client would
        // scroll through empty page after empty page and never be told it is caught up.
        bool blendTerminal = true;

        foreach(var buffer in buffers)
        {
            var settled = Settle(buffer);
            next = next.With(buffer.Tier, settled);
            blendTerminal &= settled.IsTerminal;
        }

        // The backstop, and the only tier that is not weighted. It reuses GetAllPost's filter
        // and signature, so it shares that endpoint's cached pages and its write invalidation
        // rather than minting its own.
        //
        // It runs on an empty page — the brand-new account with nothing to blend — and also
        // once every weighted tier is terminal, because HasMore cannot claim the feed is
        // exhausted without having established that the backstop is exhausted too.
        bool hasMore = !blendTerminal;

        if(page.Count == 0 || blendTerminal)
        {
            var backstop = MakeBuffer(
                FeedTier.Global,
                Builders<Post>.Filter.Eq(x => x.IsDeleted, false),
                filterSignature: null,
                preFiltered: false,
                cursor[FeedTier.Global]);

            await FetchAsync(backstop, postCollection, FetchSize(pageSize), viewer, hidden, reacted, cancellationToken);
            TakeFrom(backstop, pageSize - page.Count, page, seen);

            var settled = Settle(backstop);
            next = next.With(FeedTier.Global, settled);
            hasMore |= !settled.IsTerminal;
        }

        // Deliberately exempt from the reacted filter. This is the viewer's own newest post,
        // prepended so that posting something makes it visible immediately despite the list
        // TTL; reacting to your own post must not hide it from its own pin. Blocking is moot —
        // nobody blocks themselves — and the seen set keeps it out of the body twice over.
        var now = DateTime.UtcNow;

        // Ranked after allocation and before the pin. By this point every cursor above is
        // already settled from what was consumed, so reordering cannot disturb them — which is
        // exactly why the two steps are separated rather than folded together.
        var ranked = FeedScorer.Rank(
            page,
            nearby.ToDictionary(x => x.ShopId, x => x.Meters),
            now);

        if(latestTask is not null)
        {
            var latest = await latestTask;

            // Pinned above the ranking rather than scored into it — the point is that it is
            // always first — and only while it is recent enough for that to mean anything.
            if(latest is not null
                && now - latest.CreatedAt < PinMaxAge
                && seen.Add(latest.Id))
                ranked.Insert(0, latest);
        }

        // Unlike every other list endpoint, the cursor is returned even when HasMore is false.
        // It is the viewer's whole feed position, and the next Refresh needs its marks to know
        // what is new; dropping it at the end of the stream would restart the feed from the
        // top on the next app open. HasMore is the caught-up signal here, not a null cursor.
        var result = new CursorPageResult<PostSummaryDto>(ranked, next.Encode(), hasMore);

        return Result.Ok(await PostFeedEnricher.EnrichAsync(_dbContext, result, viewer, cancellationToken));
    }

    // One tier's fetched-but-unconsumed rows, plus the two cursors it needs. Start is the
    // position this request began from and is what the returned cursor is derived from; Walk
    // is where the next fetch round resumes and moves by fetched rows. Items carry the phase
    // that produced them, because a tier can cross from Phase A into Phase B inside a single
    // request and the two halves advance different marks.
    private sealed class TierBuffer
    {
        public FeedTier Tier { get; init; }
        public FilterDefinition<Post> Filter { get; init; } = default!;
        public string? Signature { get; init; }
        public bool PreFiltered { get; init; }
        public TierCursor Start { get; init; } = TierCursor.Empty;
        public TierCursor Walk { get; set; } = TierCursor.Empty;
        public List<(PostSummaryDto Post, FeedPhase Phase)> Items { get; } = new();
        public int Consumed { get; set; }
        public bool Fetched { get; set; }
        public bool PhaseAHasMore { get; set; } = true;
        public bool WalkedB { get; set; }
        public bool PhaseBHasMore { get; set; } = true;
    }

    private static int FetchSize(int pageSize) =>
        Math.Min(pageSize * OverFetchMultiplier, MaxFetchSize);

    private static string Mark(PostSummaryDto post) =>
        CursorCodec.Encode(post.CreatedAt, post.Id);

    private static TierBuffer MakeBuffer(
        FeedTier tier,
        FilterDefinition<Post> filter,
        string? filterSignature,
        bool preFiltered,
        TierCursor cursor) =>
        new()
        {
            Tier = tier,
            Filter = filter,
            Signature = filterSignature,
            PreFiltered = preFiltered,
            Start = cursor,
            Walk = cursor
        };

    private static FeedCursor Refresh(FeedCursor cursor, double? latitude, double? longitude)
    {
        var next = cursor;

        foreach(var tier in Enum.GetValues<FeedTier>())
        {
            next = next.With(tier, cursor[tier].OnRefresh());
        }

        var cell = latitude is not null && longitude is not null
            ? NearbyShopResolver.Cell(latitude.Value, longitude.Value)
            : default((long Lat, long Lng)?);

        return next.PinOrigin(latitude, longitude, cell?.Lat, cell?.Lng);
    }

    // Only tiers that actually have sources become buffers, which is what makes the mix row
    // fall out of availability rather than out of request parameters.
    //
    // Following and affinity drop blocked accounts from the $in itself: their signatures are
    // already per-viewer, so shrinking the list costs no cache sharing and saves the index a
    // residual filter. Trusted cannot — its $in is the same for every viewer and that is the
    // whole reason its page key is global — so it keeps the full list and drops blocked
    // accounts, and the viewer, after the read.
    private static List<TierBuffer> BuildBuffers(
        FeedCursor cursor,
        Guid viewer,
        List<Guid> following,
        List<Guid> affinity,
        List<Guid> trusted,
        List<NearbyShop> nearby,
        HashSet<Guid> hidden)
    {
        var buffers = new List<TierBuffer>();

        // Keyed by the cell rather than by the viewer, which is the whole point of the tier:
        // everyone standing on the same street shares these pages. The cell comes off the
        // cursor rather than being recomputed, so the signature and the marks can never
        // disagree about which shop set they describe.
        if(nearby.Count > 0)
        {
            var nearbyCursor = cursor[FeedTier.Nearby];

            buffers.Add(MakeBuffer(
                FeedTier.Nearby,
                Builders<Post>.Filter.And(
                    Builders<Post>.Filter.In(x => x.ShopId, nearby.Select(x => x.ShopId)),
                    Builders<Post>.Filter.Eq(x => x.IsDeleted, false)),
                $"nearby:{nearbyCursor.CellLat}:{nearbyCursor.CellLng}",
                preFiltered: false,
                nearbyCursor));
        }

        var followingIds = Visible(following, viewer, hidden);
        var affinityIds = Visible(affinity, viewer, hidden);

        if(followingIds.Count > 0)
            buffers.Add(MakeBuffer(
                FeedTier.Following,
                SourceFilter(followingIds),
                $"following:{viewer}",
                preFiltered: true,
                cursor[FeedTier.Following]));

        if(affinityIds.Count > 0)
            buffers.Add(MakeBuffer(
                FeedTier.Affinity,
                SourceFilter(affinityIds),
                $"affinity:{viewer}",
                preFiltered: true,
                cursor[FeedTier.Affinity]));

        if(trusted.Count > 0)
            buffers.Add(MakeBuffer(
                FeedTier.Trusted,
                SourceFilter(trusted),
                "trusted:global",
                preFiltered: false,
                cursor[FeedTier.Trusted]));

        return buffers;
    }

    private static List<Guid> Visible(List<Guid> source, Guid viewer, HashSet<Guid> hidden) =>
        source
            .Where(x => x != viewer && !hidden.Contains(x))
            .Distinct()
            .Take(MaxSourceAccounts)
            .ToList();

    private static FilterDefinition<Post> SourceFilter(List<Guid> accountIds) =>
        Builders<Post>.Filter.And(
            Builders<Post>.Filter.In(x => x.AccountId, accountIds),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false));

    // A tier that comes back short stays in its phase and fetches again. Shortness is not a
    // reason to change phase: Phase A can be short because blocked authors consumed the slack
    // while the paginator still has rows below, and leaving Phase A there would orphan the
    // unwalked gap permanently. Only the paginator reporting no more rows moves a tier on, and
    // TierCursor.Advance is what decides that.
    private async Task FillAsync(
        List<TierBuffer> buffers,
        IReadOnlyDictionary<FeedTier, int> quota,
        IMongoCollection<Post> posts,
        int pageSize,
        Guid viewer,
        HashSet<Guid> hidden,
        HashSet<Guid> reacted,
        CancellationToken cancellationToken)
    {
        for(int round = 0; round <= MaxRefetchRounds; round++)
        {
            var pending = buffers
                .Where(x => quota.TryGetValue(x.Tier, out var want)
                    && x.Items.Count < want
                    && !x.Walk.IsTerminal)
                .ToList();

            if(pending.Count == 0)
                return;

            await Task.WhenAll(pending.Select(x =>
                FetchAsync(x, posts, FetchSize(pageSize), viewer, hidden, reacted, cancellationToken)));
        }
    }

    private async Task FetchAsync(
        TierBuffer buffer,
        IMongoCollection<Post> posts,
        int fetchSize,
        Guid viewer,
        HashSet<Guid> hidden,
        HashSet<Guid> reacted,
        CancellationToken cancellationToken)
    {
        var phase = buffer.Walk.Phase;
        var filter = buffer.Filter;
        var lowerBound = buffer.Walk.LowerBound<Post>();

        if(lowerBound is not null)
            filter = Builders<Post>.Filter.And(filter, lowerBound);

        var request = new CursorPageRequest(buffer.Walk.Position, fetchSize);

        CursorPageResult<PostSummaryDto> result;

        if(phase == FeedPhase.A)
        {
            // Uncached on purpose. Phase A's lower bound is this viewer's own high-water mark,
            // so its page key would be shared with nobody: writing it would leave one more key
            // in Redis that is read exactly once and then dead. Phase B, whose filter carries
            // no per-viewer bound, keeps the shared cache.
            var raw = await CursorPaginator.PaginateAsync(
                posts,
                filter,
                PostProjection.Summary,
                request,
                cancellationToken);

            result = new CursorPageResult<PostSummaryDto>(
                raw.Items.Select(x => _mapper.Map<PostSummaryDto>(x)).ToList(),
                raw.NextCursor,
                raw.HasMore);
        }
        else
        {
            result = await CachedCursorPaginator.PaginateAsync<Post, PostSummaryDto>(
                _redisService,
                _mapper,
                posts,
                filter,
                PostProjection.Summary,
                request,
                CollectionName.Post,
                buffer.Signature,
                cancellationToken);
        }

        buffer.Fetched = true;

        if(phase == FeedPhase.A)
            buffer.PhaseAHasMore = result.HasMore;
        else
        {
            buffer.WalkedB = true;
            buffer.PhaseBHasMore = result.HasMore;
        }

        // The walk moves by FETCHED rows so the next round resumes in the right place. The
        // cursor handed back to the client moves by CONSUMED rows and is rebuilt from Start in
        // Settle, once allocation has decided what was actually taken.
        buffer.Walk = result.Items.Count > 0
            ? buffer.Walk.Advance(Mark(result.Items[0]), Mark(result.Items[^1]), result.HasMore)
            : buffer.Walk.Advance(null, null, result.HasMore);

        foreach(var post in result.Items)
        {
            // Reacted posts are dropped from every tier, including the two whose source list
            // was already filtered: following and affinity exclude blocked AUTHORS up front,
            // which says nothing about which of their posts this viewer has already answered.
            if(reacted.Contains(post.Id))
                continue;

            if(buffer.PreFiltered || (post.AccountId != viewer && !hidden.Contains(post.AccountId)))
                buffer.Items.Add((post, phase));
        }
    }

    // Quota first, then refill from whatever slack the other tiers are still holding, in
    // dedupe-priority order. Both passes take a contiguous prefix of each buffer — see
    // TakeFrom for why that is load-bearing rather than incidental.
    private static void Assemble(
        List<TierBuffer> buffers,
        IReadOnlyDictionary<FeedTier, int> quota,
        List<(PostSummaryDto Post, FeedTier Tier)> page,
        HashSet<Guid> seen,
        int pageSize)
    {
        var order = buffers.OrderBy(x => x.Tier).ToList();

        foreach(var buffer in order)
        {
            TakeFrom(buffer, quota.TryGetValue(buffer.Tier, out var want) ? want : 0, page, seen);
        }

        foreach(var buffer in order)
        {
            if(page.Count >= pageSize)
                return;

            TakeFrom(buffer, pageSize - page.Count, page, seen);
        }
    }

    // Consumes a CONTIGUOUS PREFIX of the buffer. Taking a non-contiguous subset would make
    // "advance to the last consumed item" skip everything between the gaps, silently and
    // permanently — which is why a cross-tier duplicate is still counted as consumed rather
    // than skipped over: the walk passed it, and leaving it out of the cursor would hand it
    // back on the next page.
    private static int TakeFrom(
        TierBuffer buffer,
        int want,
        List<(PostSummaryDto Post, FeedTier Tier)> page,
        HashSet<Guid> seen)
    {
        int took = 0;

        while(took < want && buffer.Consumed < buffer.Items.Count)
        {
            var post = buffer.Items[buffer.Consumed].Post;
            buffer.Consumed++;

            if(seen.Add(post.Id))
            {
                page.Add((post, buffer.Tier));
                took++;
            }
        }

        return took;
    }

    // Rebuilds the tier's cursor from where the request started, using only what was consumed.
    // A buffer that was never fetched keeps its cursor untouched: settling it would read the
    // default has-more flags and retire a tier that was never walked.
    private static TierCursor Settle(TierBuffer buffer)
    {
        if(!buffer.Fetched)
            return buffer.Start;

        var consumed = buffer.Items.Take(buffer.Consumed).ToList();
        var leftover = buffer.Items.Skip(buffer.Consumed).ToList();
        var next = buffer.Start;

        // The paginator's has-more answer is about what it FETCHED, and a tier fetches its
        // whole over-fetch budget to serve a quota a fraction of that size. Taking it at face
        // value retires a tier whose buffer still holds rows the page never used — they are
        // discarded with the request, the mark stays at the last consumed item, and the walk
        // never comes back for them. Unconsumed rows are themselves proof there is more below
        // the mark.
        if(next.Phase == FeedPhase.A)
        {
            var walked = consumed.Where(x => x.Phase == FeedPhase.A).ToList();

            next = next.Advance(
                walked.Count > 0 ? Mark(walked[0].Post) : null,
                walked.Count > 0 ? Mark(walked[^1].Post) : null,
                buffer.PhaseAHasMore || leftover.Any(x => x.Phase == FeedPhase.A));
        }

        if(buffer.WalkedB && next.Phase == FeedPhase.B)
        {
            var walked = consumed.Where(x => x.Phase == FeedPhase.B).ToList();

            next = next.Advance(
                null,
                walked.Count > 0 ? Mark(walked[^1].Post) : null,
                buffer.PhaseBHasMore || leftover.Any(x => x.Phase == FeedPhase.B));
        }

        return next;
    }

    // Followings are embedded on the account, so this is one document read rather than a join.
    //
    // The result is read as a raw BsonDocument rather than projected onto Account.
    // Deserializing a Following from a subdocument holding only AccountId throws
    // "No matching creator found for class domain.Following" — the entity has no creator that
    // can be satisfied without Name and Avatar. Asking for the whole Followings array would
    // deserialize, at the cost of dragging every followed account's name and avatar across the
    // wire to read one field from each.
    private async Task<List<Guid>> GetFollowingIdsAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var projection = Builders<Account>.Projection.Include("Followings.AccountId");

        var document = await accountCollection
            .Find(x => x.Id == accountId && !x.IsDeleted)
            .Project(projection)
            .FirstOrDefaultAsync(cancellationToken);

        // An account that follows nobody has no Followings field to read at all.
        if(document is null || !document.TryGetValue("Followings", out var followings))
            return new List<Guid>();

        return followings.AsBsonArray
            .Select(x => x["AccountId"].AsGuid)
            .ToList();
    }

    // $sortByCount is $group-by-count plus a descending $sort in one stage. The $match rides
    // the Reactions.AccountId multikey index, but the grouping still touches every post the
    // account has reacted to, which is why the result is cached for an hour rather than
    // recomputed per request.
    //
    // This is its own tier now rather than being unioned into the following $in. Concatenating
    // the two and capping the result put following first, so any viewer with three hundred or
    // more followings had their affinity ranking computed, cached, and then truncated away.
    private async Task<List<Guid>> GetAffinityIdsAsync(
        IMongoCollection<Post> postCollection,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        string cacheKey = CacheKey.Affinity(accountId);

        var cached = await _redisService.GetAsync<List<Guid>>(cacheKey, cancellationToken);
        if(cached is not null)
            return cached;

        var filter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.AnyEq(x => x.Reactions.Select(r => r.AccountId), accountId),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false));

        var ranked = await postCollection.Aggregate()
            .Match(filter)
            .SortByCount(x => x.AccountId)
            .Limit(AffinityLimit)
            .ToListAsync(cancellationToken);

        var accountIds = ranked.Select(x => x.Id).ToList();

        await _redisService.SetAsync(
            cacheKey,
            accountIds,
            CacheKey.DefaultAffinityTTL,
            cancellationToken);

        return accountIds;
    }

    // Unlike affinity this carries no per-viewer signal — every viewer ranks the same trusted
    // accounts the same way — so it is resolved once behind one cache key. The viewer's own id
    // and blocked accounts are dropped after the read rather than from this list, which is
    // what keeps both the list and the page key reusable across every viewer.
    private async Task<List<Guid>> GetTrustedIdsAsync(CancellationToken cancellationToken)
    {
        string cacheKey = CacheKey.TrustedAccounts();

        var cached = await _redisService.GetAsync<List<Guid>>(cacheKey, cancellationToken);
        if(cached is not null)
            return cached;

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.IsTrusted, true),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false));

        var accounts = await accountCollection
            .Find(filter)
            .SortByDescending(x => x.FollowerAmount.Value)
            .Limit(MaxTrustedAccounts)
            .Project<Account>(Builders<Account>.Projection.Include(x => x.Id))
            .ToListAsync(cancellationToken);

        var accountIds = accounts.Select(x => x.Id).ToList();

        await _redisService.SetAsync(
            cacheKey,
            accountIds,
            CacheKey.DefaultTrustedTTL,
            cancellationToken);

        return accountIds;
    }

    // Pinned outside the walk on purpose: a post created a moment ago would not otherwise show
    // up until the list TTL expired. This read is uncached and one indexed lookup, which is
    // what makes "my newest post is first after I post" hold.
    private async Task<PostSummaryDto?> GetMyLatestPostAsync(
        IMongoCollection<Post> postCollection,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Eq(x => x.AccountId, accountId),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false));

        var post = await postCollection
            .Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Project<Post>(PostProjection.Summary)
            .FirstOrDefaultAsync(cancellationToken);

        return post is null ? null : _mapper.Map<PostSummaryDto>(post);
    }
}
