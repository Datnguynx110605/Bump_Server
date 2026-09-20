using app;
using domain;
using FluentResults;
using MongoDB.Driver;

namespace infra;

public sealed class AccountService : IAccountService
{
    // How many cache invalidations are in flight at once. Bounded because the count scales
    // with the deleted account's engagement, which has no ceiling.
    private const int InvalidationBatchSize = 500;

    private readonly IMongoDbContext _dbContext;
    private readonly IR2Service _r2Service;
    private readonly IRedisService _redisService;
    private readonly IRabbitMQService _rabbitMQService;
    public AccountService(
        IMongoDbContext dbContext,
        IR2Service r2Service,
        IRedisService redisService,
        IRabbitMQService rabbitMQService)
    {
        _dbContext = dbContext;
        _r2Service = r2Service;
        _redisService = redisService;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result> ProcessAfterDeleteAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var accountProjection = Builders<Account>.Projection
            .Include(x => x.Avatars);

        var account = await accountCollection
            .Find(x => x.Id == accountId && x.IsDeleted)
            .Project<Account>(accountProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail(AppError.NotFound("Account not found"));

        foreach(var avatar in account.Avatars)
        {
            await _r2Service.DeleteAsync(
                avatar.Url,
                cancellationToken);
        }

        var refreshTokenFilter = Builders<RefreshToken>.Filter.And(
            Builders<RefreshToken>.Filter.Eq(x => x.AccountId, accountId),
            Builders<RefreshToken>.Filter.Eq(x => x.IsRevoked, false));

        var ownerCommentFilter = Builders<Comment>.Filter.And(
            Builders<Comment>.Filter.Eq(x => x.AccountId, account.Id),
            Builders<Comment>.Filter.Eq(x => x.IsDeleted, false));

        var ownerCommentUpdate = Builders<Comment>.Update
            .Set(x => x.IsDeleted, true)
            .Set(x => x.DeletedAt, DateTime.UtcNow);

        var postFilter = Builders<Post>.Filter.ElemMatch(x => x.Reactions, r => r.AccountId == account.Id);

        var postUpdate = Builders<Post>.Update
            .PullFilter(x => x.Reactions, f => f.AccountId == account.Id)
            .Inc(x => x.ReactionAmount.Value, -1);

        var ownerPostFilter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Eq(x => x.AccountId,accountId),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false));

        var ownerPostUpdate = Builders<Post>.Update
            .Set(x => x.IsDeleted, true)
            .Set(x => x.DeletedAt, DateTime.UtcNow);

        var commentFilter = Builders<Comment>.Filter.ElemMatch(x => x.Reactions, r => r.AccountId == account.Id);

        var commentUpdate = Builders<Comment>.Update
            .PullFilter(x => x.Reactions, f => f.AccountId == account.Id)
            .Inc(x => x.ReactionAmount.Value, -1);

        var followerFilter = Builders<Account>.Filter.ElemMatch(x => x.Followers, r => r.AccountId == account.Id);

        var followerUpdate = Builders<Account>.Update
            .PullFilter(x => x.Followers, f => f.AccountId == account.Id)
            .Inc(x => x.FollowerAmount.Value, -1);

        var followingFilter = Builders<Account>.Filter.ElemMatch(x => x.Followings, r => r.AccountId == account.Id);

        var followingUpdate = Builders<Account>.Update
            .PullFilter(x => x.Followings, f => f.AccountId == account.Id)
            .Inc(x => x.FollowingAmount.Value, -1);

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var refreshTokenCollection = _dbContext.GetCollection<RefreshToken>(CollectionName.RefreshToken);

        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        var notificationCollection = _dbContext.GetCollection<Notification>(CollectionName.Notification);

        // Started together rather than awaited in turn: five reads across three collections,
        // none of which needs another's answer, so they cost one wall-clock wait instead of
        // five round trips back to back.
        //
        // ownerComments is read BEFORE the flip on purpose, unlike the cascade lists below:
        // it drives the CommentAmount decrement, which must apply exactly once. A redelivery
        // finds these rows already IsDeleted and reads an empty list, which is what keeps the
        // decrement from double-applying and driving the counter negative.
        var ownerCommentsTask = commentCollection
            .Find(ownerCommentFilter)
            .Project<Comment>(Builders<Comment>.Projection
                .Include(x => x.Id)
                .Include(x => x.PostId))
            .ToListAsync(cancellationToken);

        var reactedPostsTask = postCollection
            .Find(postFilter)
            .Project<Post>(Builders<Post>.Projection.Include(x => x.Id))
            .ToListAsync(cancellationToken);

        var reactedCommentsTask = commentCollection
            .Find(commentFilter)
            .Project<Comment>(Builders<Comment>.Projection.Include(x => x.Id))
            .ToListAsync(cancellationToken);

        var followerAccountsTask = accountCollection
            .Find(followerFilter)
            .Project<Account>(Builders<Account>.Projection.Include(x => x.Id))
            .ToListAsync(cancellationToken);

        var followingAccountsTask = accountCollection
            .Find(followingFilter)
            .Project<Account>(Builders<Account>.Projection.Include(x => x.Id))
            .ToListAsync(cancellationToken);

        await Task.WhenAll(
            ownerCommentsTask,
            reactedPostsTask,
            reactedCommentsTask,
            followerAccountsTask,
            followingAccountsTask);

        var ownerComments = ownerCommentsTask.Result;
        var reactedPosts = reactedPostsTask.Result;
        var reactedComments = reactedCommentsTask.Result;
        var followerAccounts = followerAccountsTask.Result;
        var followingAccounts = followingAccountsTask.Result;

        await Task.WhenAll(
            // Deleted rather than marked revoked, for the reason ChangePasswordHandler
            // spells out: a revoked row is the replay signal, and an account deletion is not
            // a replay.
            refreshTokenCollection.DeleteManyAsync(
                refreshTokenFilter,
                cancellationToken),
            postCollection.UpdateManyAsync(
                postFilter,
                postUpdate,
                cancellationToken : cancellationToken),
            postCollection.UpdateManyAsync(
                ownerPostFilter,
                ownerPostUpdate,
                cancellationToken : cancellationToken),
            commentCollection.UpdateManyAsync(
                commentFilter,
                commentUpdate,
                cancellationToken : cancellationToken),
            commentCollection.UpdateManyAsync(
                ownerCommentFilter,
                ownerCommentUpdate,
                cancellationToken : cancellationToken),
            accountCollection.UpdateManyAsync(
                followerFilter,
                followerUpdate,
                cancellationToken : cancellationToken),
            accountCollection.UpdateManyAsync(
                followingFilter,
                followingUpdate,
                cancellationToken : cancellationToken));

        // The UpdateMany above only flipped IsDeleted, which is all Mongo can do: it hides the
        // rows from every read in two commands, and nothing more. These messages carry the
        // side effects it cannot express — the images in R2, which no read filter knows about
        // and the retention sweep would otherwise orphan, and other accounts' comments on
        // these posts, which ownerCommentFilter does not match and which would survive as live
        // rows pointing at a hard-deleted post.
        //
        // One message per row rather than a loop here: post and comment counts are unbounded,
        // so inline this becomes a single message whose cost scales with them, and a failure
        // near the end replays all of it. Both workers require the row to already be
        // IsDeleted, which is why the publish follows the update rather than replacing it.
        //
        // Enumerated on IsDeleted == true, and read again here rather than reusing a list
        // taken before the flip. That ordering was the whole bug: the pre-flip list is empty
        // on every redelivery, because the first attempt already flipped the rows, so a crash
        // or a broker blip between the update above and this loop lost the cascade for good —
        // the images stayed in R2 with no document left to name them, and other accounts'
        // comments survived pointing at a post the sweep would hard-delete. Reading the
        // post-state is what makes a retry republish instead of finding nothing.
        //
        // The cost is that this also names rows the owner deleted individually long ago,
        // whose cascade already ran. Both workers are no-ops on a second pass — S3 answers a
        // delete of a missing key with success, and the comment sweep matches nothing — so
        // that is redundant messages, not a wrong result.
        var cascadePostsTask = postCollection
            .Find(Builders<Post>.Filter.And(
                Builders<Post>.Filter.Eq(x => x.AccountId, accountId),
                Builders<Post>.Filter.Eq(x => x.IsDeleted, true)))
            .Project<Post>(Builders<Post>.Projection.Include(x => x.Id))
            .ToListAsync(cancellationToken);

        var cascadeCommentsTask = commentCollection
            .Find(Builders<Comment>.Filter.And(
                Builders<Comment>.Filter.Eq(x => x.AccountId, accountId),
                Builders<Comment>.Filter.Eq(x => x.IsDeleted, true)))
            .Project<Comment>(Builders<Comment>.Projection.Include(x => x.Id))
            .ToListAsync(cancellationToken);

        await Task.WhenAll(cascadePostsTask, cascadeCommentsTask);

        var cascadePosts = cascadePostsTask.Result;
        var cascadeComments = cascadeCommentsTask.Result;

        foreach(var post in cascadePosts)
        {
            await _rabbitMQService.PublishAsync(
                QueueName.DeletePost,
                new PostMessage(Guid.NewGuid(), post.Id),
                cancellationToken);
        }

        foreach(var comment in cascadeComments)
        {
            await _rabbitMQService.PublishAsync(
                QueueName.DeleteComment,
                new CommentMessage(Guid.NewGuid(), account.Id, comment.Id),
                cancellationToken);
        }

        // Hard delete rather than soft: once the account is gone nothing can ever read its
        // notifications again, and the collection has no retention of its own.
        await notificationCollection.DeleteManyAsync(
            Builders<Notification>.Filter.Eq(x => x.ToAccountId, account.Id),
            cancellationToken);

        // Hard-deleted for the same reason the notifications above are: a deleted account's
        // tokens are worthless, and leaving them would keep the phone receiving pushes until
        // APNs happened to answer with a 410, which it may never do.
        await _dbContext.GetCollection<DeviceToken>(CollectionName.DeviceToken).DeleteManyAsync(
            Builders<DeviceToken>.Filter.Eq(x => x.AccountId, account.Id),
            cancellationToken);

        var commentCountByPost = ownerComments
            .GroupBy(x => x.PostId)
            .ToList();

        if(commentCountByPost.Count > 0)
        {
            // Gte the amount being subtracted, for the reason spelled out in
            // DeleteCommentHandler: this decrement is not coupled to the array it summarises,
            // so it is the one that can push the counter past zero into a value the domain
            // cannot construct.
            var commentAmountWrites = commentCountByPost
                .Select(group => new UpdateOneModel<Post>(
                    Builders<Post>.Filter.And(
                        Builders<Post>.Filter.Eq(x => x.Id, group.Key),
                        Builders<Post>.Filter.Gte(x => x.CommentAmount.Value, group.Count())),
                    Builders<Post>.Update.Inc(x => x.CommentAmount.Value, -group.Count())))
                .ToList<WriteModel<Post>>();

            await postCollection.BulkWriteAsync(commentAmountWrites, cancellationToken : cancellationToken);
        }

        var cacheInvalidations = new List<Task>
        {
            _redisService.RemoveAsync(CacheKey.Account(account.Id), cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Notification,
                $"account:{account.Id}",
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Comment,
                $"account:{account.Id}",
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                $"account:{account.Id}",
                cancellationToken),
            // The cascade soft-deletes the owner's posts, drops their reactions from other
            // people's posts and decrements CommentAmount on every post they commented on —
            // all three are visible in the all-posts listing, so post:all has to be retired
            // and not just this account's own list.
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                filterSignature: null,
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                $"{CollectionName.Account}:follower",
                $"account:{account.Id}",
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                $"{CollectionName.Account}:following",
                $"account:{account.Id}",
                cancellationToken)
        };

        foreach(var post in reactedPosts)
        {
            cacheInvalidations.Add(_redisService.RemoveAsync(CacheKey.Post(post.Id), cancellationToken));
            cacheInvalidations.Add(ListCache.InvalidateAsync(
    _redisService,
    $"{CollectionName.Post}:reaction",
    $"post:{post.Id}",
    cancellationToken));
        }

        foreach(var comment in reactedComments)
        {
            cacheInvalidations.Add(_redisService.RemoveAsync(CacheKey.Comment(comment.Id), cancellationToken));
            cacheInvalidations.Add(ListCache.InvalidateAsync(
    _redisService,
    $"{CollectionName.Comment}:reaction",
    $"comment:{comment.Id}",
    cancellationToken));
        }

        foreach(var comment in ownerComments)
        {
            cacheInvalidations.Add(_redisService.RemoveAsync(CacheKey.Comment(comment.Id), cancellationToken));
        }

        foreach(var post in cascadePosts)
        {
            cacheInvalidations.Add(_redisService.RemoveAsync(CacheKey.Post(post.Id), cancellationToken));
        }

        foreach(var group in commentCountByPost)
        {
            cacheInvalidations.Add(_redisService.RemoveAsync(CacheKey.Post(group.Key), cancellationToken));
            cacheInvalidations.Add(ListCache.InvalidateAsync(
    _redisService,
    CollectionName.Comment,
    $"post:{group.Key}",
    cancellationToken));
        }

        foreach(var follower in followerAccounts)
        {
            cacheInvalidations.Add(_redisService.RemoveAsync(CacheKey.Account(follower.Id), cancellationToken));
            cacheInvalidations.Add(ListCache.InvalidateAsync(
    _redisService,
    $"{CollectionName.Account}:follower",
    $"account:{follower.Id}",
    cancellationToken));
        }

        foreach(var following in followingAccounts)
        {
            cacheInvalidations.Add(_redisService.RemoveAsync(CacheKey.Account(following.Id), cancellationToken));
            cacheInvalidations.Add(ListCache.InvalidateAsync(
    _redisService,
    $"{CollectionName.Account}:following",
    $"account:{following.Id}",
    cancellationToken));
        }

        // Chunked rather than one Task.WhenAll over the whole list. Every loop above adds up
        // to two entries per row, and reacted posts, reacted comments and followers are all
        // unbounded, so a heavily engaged account produced a WhenAll of hundreds of thousands
        // of commands queued on one multiplexer at once — which is how the backlog times out
        // rather than how it goes fast.
        foreach(var batch in cacheInvalidations.Chunk(InvalidationBatchSize))
            await Task.WhenAll(batch);

        return Result.Ok();
    }
}
