using app;
using domain;
using FluentResults;
using MongoDB.Bson;
using MongoDB.Driver;

namespace infra;

// Refreshes the name and avatar copies that follow, block and react denormalize onto other
// documents at write time.
//
// Those copies were written once and never revisited, so renaming an account left the old
// name in every follower list, following list, block list and reaction list that already
// contained it — permanently, because no read path resolves the account and no write path
// looked for the copies. Changing an avatar was worse: the superseded image kept rendering
// from those same snapshots.
//
// Deferred rather than run in the handler because the document count is unbounded — it
// scales with how many people follow the account and how much it has reacted to — and a
// rename must not make the request that triggered it wait on that.
public sealed class ProfileService : IProfileService
{
    private readonly IMongoDbContext _dbContext;
    public ProfileService(IMongoDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> ProcessProfileChangedAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var projection = Builders<Account>.Projection
            .Include(x => x.Name)
            .Include(x => x.Avatars);

        var account = await accountCollection
            .Find(x => x.Id == accountId && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail(AppError.NotFound("Account not found"));

        Name name = account.Name;

        // LastOrDefault, the same way every writer of these snapshots picks the current
        // avatar. Null is a real value here — an account that removed its avatar must clear
        // the copies, not keep the last one it had.
        Avatar? avatar = account.Avatars.LastOrDefault();

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);
        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);
        var notificationCollection = _dbContext.GetCollection<Notification>(CollectionName.Notification);

        // Five array refreshes plus the notification avatar. Each is one UpdateMany with a
        // filtered positional operator, so the cost is in the documents matched rather than
        // in the number of commands, and a redelivery writes the same values again.
        await Task.WhenAll(
            RefreshArrayAsync(accountCollection, nameof(Account.Followers), accountId, name, avatar, cancellationToken),
            RefreshArrayAsync(accountCollection, nameof(Account.Followings), accountId, name, avatar, cancellationToken),
            RefreshArrayAsync(accountCollection, nameof(Account.Blocks), accountId, name, avatar, cancellationToken),
            RefreshArrayAsync(postCollection, nameof(Post.Reactions), accountId, name, avatar, cancellationToken),
            RefreshArrayAsync(commentCollection, nameof(Comment.Reactions), accountId, name, avatar, cancellationToken),
            // Only the avatar. A notification's Content is a sentence rendered at the time of
            // the event — "<name> commented on your post" — so the name in it is part of a
            // historical record rather than a stale copy, and rewriting it would mean
            // re-rendering past events.
            notificationCollection.UpdateManyAsync(
                Builders<Notification>.Filter.Eq(x => x.ActorAccountId, accountId),
                Builders<Notification>.Update.Set(x => x.ActorAvatar, avatar),
                cancellationToken : cancellationToken));

        // No list-cache invalidation. Retiring the follower list of every account this one
        // follows would mean enumerating them, which is the unbounded fan-out this worker
        // exists to avoid; the stored rows are correct the moment this returns and the
        // cached pages carrying the old value expire on DefaultListTTL, two minutes. The
        // account's own entity key is already evicted by the handler that published this.
        return Result.Ok();
    }

    // Followers, Followings, Blocks and Reactions are four names for one shape — AccountId,
    // Name, Avatar — so one helper covers all of them.
    private static Task RefreshArrayAsync<TDocument>(
        IMongoCollection<TDocument> collection,
        string arrayField,
        Guid accountId,
        Name name,
        Avatar? avatar,
        CancellationToken cancellationToken)
    {
        var filter = Builders<TDocument>.Filter.Eq($"{arrayField}.AccountId", accountId);

        var update = Builders<TDocument>.Update
            .Set($"{arrayField}.$[entry].Name", name)
            .Set($"{arrayField}.$[entry].Avatar", avatar);

        // The array filter is a raw document, so it does not pass through the class map that
        // gives every other Guid in this codebase its standard-UUID binary form. Written
        // explicitly for that reason: the default representation would not match the stored
        // value and the update would silently touch nothing.
        var options = new UpdateOptions
        {
            ArrayFilters = new[]
            {
                new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    new BsonDocument("entry.AccountId", new BsonBinaryData(accountId, GuidRepresentation.Standard)))
            }
        };

        return collection.UpdateManyAsync(filter, update, options, cancellationToken);
    }
}
