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

        var followingIds = await GetFollowingIdsAsync(identity.Value, cancellationToken);
        var affinityIds = await GetAffinityIdsAsync(postCollection, identity.Value, cancellationToken);

        // Reacting to your own post is allowed, so affinity can name the reader. Their own
        // posts belong in GetMyPost, not in a feed about everyone else.
        var sourceIds = followingIds
            .Concat(affinityIds)
            .Where(x => x != identity.Value)
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

        return Result.Ok(await PostFeedEnricher.EnrichAsync(_dbContext, dtoPage, identity.Value, cancellationToken));
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
}
