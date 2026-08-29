using domain;
using MongoDB.Driver;

namespace app;

public static class SaveMembership
{
    public static async Task<bool> HasSavedPostAsync(
        IMongoCollection<Account> accounts,
        Guid accountId,
        Guid postId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, accountId),
            Builders<Account>.Filter.AnyEq(x => x.SavePosts.Select(sp => sp.PostId), postId));

        return await accounts.Find(filter).AnyAsync(cancellationToken);
    }

    public static async Task<bool> HasSavedShopAsync(
        IMongoCollection<Account> accounts,
        Guid accountId,
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, accountId),
            Builders<Account>.Filter.AnyEq(x => x.SaveShops.Select(ss => ss.ShopId), shopId));

        return await accounts.Find(filter).AnyAsync(cancellationToken);
    }

    public static async Task<HashSet<Guid>> SavedPostIdsAsync(
        IMongoCollection<Account> accounts,
        Guid accountId,
        IReadOnlyCollection<Guid> postIds,
        CancellationToken cancellationToken)
    {
        if(postIds.Count == 0)
            return new HashSet<Guid>();

        var projection = Builders<Account>.Projection.Include(x => x.SavePosts);

        var account = await accounts
            .Find(x => x.Id == accountId && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return new HashSet<Guid>();

        var requested = postIds.ToHashSet();
        return account.SavePosts.Select(sp => sp.PostId).Where(requested.Contains).ToHashSet();
    }
}
