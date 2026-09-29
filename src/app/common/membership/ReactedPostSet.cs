using domain;
using MongoDB.Driver;

namespace app;

// The posts a viewer has already reacted to. The feed excludes them, because reacting is the
// clearest signal there is that a post has been seen and dealt with.
//
// Built the same way as BlockedAccountSet and for the same reasons: read the cache, fall back
// to Mongo, cache the answer, and let the writer delete the key rather than waiting for a TTL.
// Rebuilding from the reactions themselves rather than accumulating a list makes this
// self-healing — a cold or evicted Redis loses nothing, and reactions made before this shipped
// are covered without a migration or a backfill.
public static class ReactedPostSet
{
    // ponytail: capped, so a viewer past the cap starts seeing their oldest reactions again.
    // The cap is what bounds both the cached payload and the per-page filter that reads it.
    // If real reaction counts ever approach it, the upgrade is a per-viewer reaction
    // collection with its own index rather than a larger list.
    private const int MaxReactedPosts = 500;

    public static async Task<IReadOnlyList<Guid>> ResolveAsync(
        IRedisService redisService,
        IMongoDbContext dbContext,
        Guid viewerId,
        CancellationToken cancellationToken)
    {
        string cacheKey = CacheKey.ReactedPosts(viewerId);

        var cached = await redisService.GetAsync<List<Guid>>(cacheKey, cancellationToken);
        if(cached is not null)
            return cached;

        var postCollection = dbContext.GetCollection<Post>(CollectionName.Post);

        var filter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.AnyEq(x => x.Reactions.Select(r => r.AccountId), viewerId),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false));

        // Deliberately unsorted. The Reactions.AccountId multikey index answers the match, but
        // no index carries that field alongside CreatedAt, so asking for newest-first would add
        // a blocking in-memory sort over whole post documents — arrays, images and all — on
        // every cold read. Which five hundred the cap keeps is not load-bearing for an
        // exclusion set, and the cost of getting it in a useful order is.
        var posts = await postCollection
            .Find(filter)
            .Limit(MaxReactedPosts)
            .Project<Post>(Builders<Post>.Projection.Include(x => x.Id))
            .ToListAsync(cancellationToken);

        var postIds = posts.Select(x => x.Id).ToList();

        await redisService.SetAsync(
            cacheKey,
            postIds,
            CacheKey.DefaultReactedPostsTTL,
            cancellationToken);

        return postIds;
    }
}
