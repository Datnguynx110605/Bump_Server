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

        // ponytail: reads the viewer's whole SavePosts array to answer a membership question
        // about one page, and that array grows without bound. Not narrowed server-side
        // because the obvious narrowing is wrong: an $elemMatch projection returns only the
        // FIRST matching element, which would mark at most one post per page as saved. The
        // correct form is a $filter aggregation expression over the array, which needs the
        // page's ids encoded as standard-UUID binary — verify that against a live MongoDB
        // before switching, since a translation failure takes the whole feed down.
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
