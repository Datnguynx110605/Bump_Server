using System.Security.Cryptography;
using System.Text;
using domain;
using MongoDB.Driver;

namespace app;

// The accounts a viewer must not see: the union of the accounts they blocked and the
// accounts that blocked them. Every filtered read needs it, and it only changes when
// BlockService writes, so it is cached per viewer and deleted there rather than expiring.
public static class BlockedAccountSet
{
    // ponytail: capped, so past the cap a viewer starts seeing accounts they blocked.
    // $nin rides as a residual filter on the indexed predicate and the cap is what keeps
    // that bounded; the upgrade path if real block lists ever approach it is to
    // denormalize a HiddenFrom array onto posts and comments and index that instead.
    // The same 300 cap and reasoning as the $in in GetRelatedPostHandler.
    private const int MaxHiddenAccounts = 300;

    public static async Task<IReadOnlyList<Guid>> ResolveAsync(
        IRedisService redisService,
        IMongoDbContext dbContext,
        Guid viewerId,
        CancellationToken cancellationToken)
    {
        string cacheKey = CacheKey.BlockedSet(viewerId);

        var cached = await redisService.GetAsync<List<Guid>>(cacheKey, cancellationToken);
        if(cached is not null)
            return cached;

        var accountCollection = dbContext.GetCollection<Account>(CollectionName.Account);

        // Whole subdocuments, not Blocks.AccountId alone: a Block projected down to its id
        // has no satisfiable creator and throws on deserialize, the same trap documented
        // for Following in GetRelatedPostHandler. Dragging the snapshots is affordable here
        // because the result is capped and then cached for a day.
        var projection = Builders<Account>.Projection
            .Include(x => x.Blocks)
            .Include(x => x.BlockedBy);

        var account = await accountCollection
            .Find(x => x.Id == viewerId && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        // Sorted so that two viewers hiding the same accounts produce the same signature
        // below and therefore share cached pages.
        var hidden = account is null
            ? new List<Guid>()
            : account.Blocks.Select(x => x.AccountId)
                .Concat(account.BlockedBy)
                .Distinct()
                .OrderBy(x => x)
                .Take(MaxHiddenAccounts)
                .ToList();

        await redisService.SetAsync(
            cacheKey,
            hidden,
            CacheKey.DefaultBlockedSetTTL,
            cancellationToken);

        return hidden;
    }

    // The page cache is keyed by what is hidden rather than by who is viewing. Two
    // consequences: an empty set returns the caller's signature untouched, so everyone who
    // blocks nobody keeps sharing the one cached page; and a block changes the key the
    // instant it lands, so no page-cache invalidation is needed on block at all.
    public static string? Signature(string? filterSignature, IReadOnlyList<Guid> hidden)
    {
        if(hidden.Count == 0)
            return filterSignature;

        // Sorted here as well as in ResolveAsync so the invariant is local: an unsorted
        // caller would otherwise get its own cache namespace for an identical set, which
        // fails as a silent cache miss rather than as anything visible.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',', hidden.Order())));

        return $"{filterSignature ?? "all"}:hidden:{Convert.ToHexString(digest)[..16].ToLowerInvariant()}";
    }
}
