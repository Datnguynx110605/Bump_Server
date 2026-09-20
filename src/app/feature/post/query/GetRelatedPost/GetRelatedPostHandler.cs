using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Bson;
using MongoDB.Driver;

namespace app;

// Posts from the accounts this account follows, plus the accounts it reacts to most.
//
// Only the second half is precomputed, because only the second half is expensive: the
// followed ids are already embedded on the account document and cost one projected read,
// while ranking reaction targets means walking every post the account ever reacted to.
// What gets cached is that ranking — a short list of account ids — and not the feed
// itself, which stays a single indexed $in query and therefore stays correct the moment
// a followed account posts.
internal sealed class GetRelatedPostHandler
    : IRequestHandler<GetRelatedPostQuery, Result<CursorPageResult<PostFeedItemDto>>>
{
    private const int AffinityLimit = 20;

    // Mongo scans the index once per value in an $in and merge-sorts the results, so the
    // source list has to stay bounded no matter how many accounts a user follows.
    private const int MaxSourceAccounts = 300;

    // Same reasoning as MaxSourceAccounts, for the trusted-account fallback's $in.
    private const int MaxTrustedAccounts = 200;

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

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        // Started together rather than awaited in turn: they read different collections and
        // share no state, so the second no longer waits out the first's round trip.
        var hiddenTask = BlockedAccountSet.ResolveAsync(
            _redisService,
            _dbContext,
            identity.Value,
            cancellationToken);

        var followingTask = GetFollowingIdsAsync(identity.Value, cancellationToken);
        var affinityTask = GetAffinityIdsAsync(postCollection, identity.Value, cancellationToken);

        // Started here rather than only when tier 1 turns out empty: it is a single Redis
        // read behind a global cache key (same list for every viewer), so overlapping it
        // with the reads above costs nothing on the common path, but it shaves a full
        // round trip off the fallback path below for every request that needs it.
        var trustedTask = GetTrustedIdsAsync(cancellationToken);

        // Depends on nothing but the identity, so it starts here and overlaps the feed query
        // instead of trailing it. Only on the first page — the only page it can be prepended
        // to — so later pages do not pay for a read they will discard.
        var latestTask = request.pageRequest.Cursor is null
            ? GetMyLatestPostAsync(postCollection, identity.Value, cancellationToken)
            : null;

        var followingIds = await followingTask;
        var affinityIds = await affinityTask;
        var hidden = (await hiddenTask).ToHashSet();

        // Reacting to your own post is allowed, so affinity can name the reader. The reader's
        // own posts are not part of the feed body — only their latest one, pinned on top.
        // Blocked accounts are dropped from the source list rather than excluded with a
        // $nin: this feed already names its authors, so removing them shrinks the $in
        // instead of adding a residual filter the index cannot help with. The signature is
        // already per-account, so no cache key changes.
        var sourceIds = followingIds
            .Concat(affinityIds)
            .Where(x => x != identity.Value && !hidden.Contains(x))
            .Distinct()
            .Take(MaxSourceAccounts)
            .ToList();

        // Per-account signature, so nothing else in the codebase bumps its version
        // counter: unlike the shared feeds, this list retires on DefaultListTTL alone.
        // Two minutes of staleness is the price of not fanning an invalidation out to
        // every follower on each new post.
        string filterSignature = $"related:{identity.Value}";

        var filter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.In(x => x.AccountId, sourceIds),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false));

        var dtoPage = await CachedCursorPaginator.PaginateAsync<Post, PostSummaryDto>(
            _redisService,
            _mapper,
            postCollection,
            filter,
            PostProjection.Summary,
            request.pageRequest,
            CollectionName.Post,
            filterSignature,
            cancellationToken);

        // An account that follows nobody and has reacted to nobody has nothing to relate to,
        // and so does one whose followed accounts have never posted. Rather than hand back an
        // empty home, fall back to platform-trusted accounts and, failing that, the reader's
        // own posts, newest first.
        //
        // The condition is "the page came back empty", not "sourceIds is empty", because the
        // second case is invisible to sourceIds. Testing it on every page rather than only the
        // first is what keeps the fallback pageable: CursorPaginator ends a stream with
        // NextCursor null, so a normal feed never yields an empty page mid-stream, and the
        // condition therefore re-derives the same way for page 2 as it did for page 1.
        bool ownPostsFallback = false;

        if(dtoPage.Items.Count == 0)
        {
            var trustedIds = (await trustedTask)
                .Where(x => x != identity.Value && !hidden.Contains(x))
                .ToList();

            if(trustedIds.Count > 0)
                dtoPage = await CachedCursorPaginator.PaginateAsync<Post, PostSummaryDto>(
                    _redisService,
                    _mapper,
                    postCollection,
                    Builders<Post>.Filter.And(
                        Builders<Post>.Filter.In(x => x.AccountId, trustedIds),
                        Builders<Post>.Filter.Eq(x => x.IsDeleted, false)),
                    PostProjection.Summary,
                    request.pageRequest,
                    CollectionName.Post,
                    $"trusted:{identity.Value}",
                    cancellationToken);

            if(dtoPage.Items.Count == 0)
            {
                ownPostsFallback = true;

                // Same filter and signature as GetMyPost, so the two share cached pages — and
                // the pin below is unnecessary here: post create, edit and delete all bump
                // "account:{id}", so a just-created post is already at the top of this stream.
                dtoPage = await CachedCursorPaginator.PaginateAsync<Post, PostSummaryDto>(
                    _redisService,
                    _mapper,
                    postCollection,
                    Builders<Post>.Filter.And(
                        Builders<Post>.Filter.Eq(x => x.AccountId, identity.Value),
                        Builders<Post>.Filter.Eq(x => x.IsDeleted, false)),
                    PostProjection.Summary,
                    request.pageRequest,
                    CollectionName.Post,
                    $"account:{identity.Value}",
                    cancellationToken);
            }
        }

        // Pinned outside the paginator on purpose: the related page is cached for
        // DefaultListTTL under a signature nothing bumps, so a post created a moment ago would
        // not show up in it. This read is uncached and one indexed lookup, which is what makes
        // "my newest post is always first" hold. Skipped for the own-posts fallback, where it
        // would duplicate the one post that feed already contains.
        // Awaited on both paths, prepended on only one. The fallback used to return without
        // ever awaiting it, which left a started Mongo query with nobody observing its result
        // or its exception.
        if(latestTask is not null)
        {
            var latest = await latestTask;

            if(!ownPostsFallback && latest is not null)
                dtoPage = dtoPage with { Items = dtoPage.Items.Prepend(latest).ToList() };
        }

        return Result.Ok(await PostFeedEnricher.EnrichAsync(_dbContext, dtoPage, identity.Value, cancellationToken));
    }

    // The cursor on dtoPage still belongs to the last feed item, so prepending here does not
    // disturb paging: the pinned post is never part of the stream the cursor walks.
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

    // Followings are embedded on the account, so this is one document read rather than a
    // join. The projection matters: the full account document also carries followers,
    // saved posts, saved shops and avatars, none of which this needs.
    //
    // The result is read as a raw BsonDocument rather than projected onto Account.
    // Deserializing a Following from a subdocument holding only AccountId throws
    // "No matching creator found for class domain.Following" — the entity has no creator
    // that can be satisfied without Name and Avatar. Asking for the whole Followings
    // array would deserialize, at the cost of dragging every follower's name and avatar
    // across the wire to read one field from each.
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

    // $sortByCount is $group-by-count plus a descending $sort in one stage. The $match
    // rides the Reactions.AccountId multikey index, but the grouping still touches every
    // post the account has reacted to, which is why the result is cached for an hour
    // rather than recomputed per request.
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

    // Unlike affinity, this carries no per-viewer signal — every viewer ranks the same
    // trusted accounts the same way — so it is resolved once behind one cache key rather
    // than per account. The caller excludes the viewer's own id and blocked accounts
    // afterward, which is what keeps this list reusable across every viewer.
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
}
