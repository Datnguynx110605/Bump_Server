using domain;
using MongoDB.Bson;
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

        // Narrowed in the server rather than by reading the whole SavePosts array back and
        // filtering in process. That array grows without bound while the question is only
        // ever about one page, so a reader with ten thousand saved posts was dragging all of
        // them across the wire on every feed request to answer twenty membership questions.
        //
        // $filter rather than an $elemMatch projection: $elemMatch returns only the FIRST
        // matching element, which would mark at most one post per page as saved. $map then
        // reduces each survivor to its id so nothing but the ids come back.
        //
        // The ids in the $in are written as explicit standard-UUID binary. This stage is a
        // raw document and does not pass through the class map that gives every other Guid
        // here its representation, and a mismatch does not fail — it matches nothing, and
        // the feed quietly reports every post as unsaved.
        var savedIds = new BsonArray(postIds.Select(id =>
            new BsonBinaryData(id, GuidRepresentation.Standard)));

        var projectStage = new BsonDocument("$project", new BsonDocument(
            "PostIds", new BsonDocument("$map", new BsonDocument
            {
                { "input", new BsonDocument("$filter", new BsonDocument
                    {
                        // An account that has never saved anything has no SavePosts field at
                        // all, and $filter errors on a missing input rather than treating it
                        // as empty.
                        { "input", new BsonDocument("$ifNull", new BsonArray { "$SavePosts", new BsonArray() }) },
                        { "as", "sp" },
                        { "cond", new BsonDocument("$in", new BsonArray { "$$sp.PostId", savedIds }) }
                    })
                },
                { "as", "sp" },
                { "in", "$$sp.PostId" }
            })));

        var document = await accounts.Aggregate()
            .Match(x => x.Id == accountId && !x.IsDeleted)
            .AppendStage<BsonDocument>(projectStage)
            .FirstOrDefaultAsync(cancellationToken);

        if(document is null || !document.TryGetValue("PostIds", out var matched))
            return new HashSet<Guid>();

        return matched.AsBsonArray.Select(x => x.AsGuid).ToHashSet();
    }
}
