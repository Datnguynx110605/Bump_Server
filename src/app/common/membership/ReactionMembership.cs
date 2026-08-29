using domain;
using MongoDB.Driver;

namespace app;

public static class ReactionMembership
{
    public static async Task<bool> HasReactedToPostAsync(
        IMongoCollection<Post> posts,
        Guid postId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Eq(x => x.Id, postId),
            Builders<Post>.Filter.AnyEq(x => x.Reactions.Select(r => r.AccountId), accountId));

        return await posts.Find(filter).AnyAsync(cancellationToken);
    }

    public static async Task<HashSet<Guid>> ReactedPostIdsAsync(
        IMongoCollection<Post> posts,
        IReadOnlyCollection<Guid> postIds,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        if(postIds.Count == 0)
            return new HashSet<Guid>();

        var filter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.In(x => x.Id, postIds),
            Builders<Post>.Filter.AnyEq(x => x.Reactions.Select(r => r.AccountId), accountId));

        var projection = Builders<Post>.Projection.Include(x => x.Id);

        var matched = await posts
            .Find(filter)
            .Project<Post>(projection)
            .ToListAsync(cancellationToken);

        return matched.Select(x => x.Id).ToHashSet();
    }
}
