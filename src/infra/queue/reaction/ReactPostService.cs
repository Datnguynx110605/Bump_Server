using app;
using domain;
using FluentResults;
using MongoDB.Driver;

namespace infra;

public sealed class ReactPostService : IReactPostService
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly INotificationService _notificationService;
    public ReactPostService(
        IMongoDbContext dbContext,
        IRedisService redisService,
        INotificationService notificationService)
    {
        _dbContext = dbContext;
        _redisService = redisService;
        _notificationService = notificationService;
    }

    public async Task<Result> ProcessReactPostAsync(
        Guid accountId,
        Guid postId,
        CancellationToken cancellationToken)
    {
        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var accountProjection = Builders<Account>.Projection
            .Include(x => x.Name)
            .Include(x => x.Avatars);

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        // Read before the write rather than after it. The author id is what the guard
        // compares against, and this read already existed below for the notification, so
        // moving it up costs nothing.
        var postOwnerProjection = Builders<Post>.Projection
            .Include(x => x.AccountId)
            .Include(x => x.ShopId);

        // Started together: different collections, neither needs the other's answer. Only
        // the block guard below depends on both, so overlapping these takes the path from
        // four sequential round trips to three.
        var accountTask = accountCollection
            .Find(x => x.Id == accountId && !x.IsDeleted)
            .Project<Account>(accountProjection)
            .FirstOrDefaultAsync(cancellationToken);

        var postTask = postCollection
            .Find(x => x.Id == postId && !x.IsDeleted)
            .Project<Post>(postOwnerProjection)
            .FirstOrDefaultAsync(cancellationToken);

        await Task.WhenAll(accountTask, postTask);

        var account = accountTask.Result;
        var post = postTask.Result;

        if(account is null)
            return Result.Fail(AppError.NotFound("Account not found"));

        if(post is null)
            return Result.Fail(AppError.NotFound("Post not found"));

        // Silently refused, not reported: the endpoint already answered 202, and an error
        // here would tell the actor that the other account blocked them. A failed Result is
        // logged by QueueConsumerHost and the message is not retried.
        if(await BlockGuard.IsBlockedAsync(accountCollection, accountId, post.AccountId, cancellationToken))
            return Result.Fail("Blocked by or blocking this account");

        var filter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Eq(x => x.Id, postId),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Post>.Filter.Not(Builders<Post>.Filter.AnyEq(x => x.Reactions.Select(r => r.AccountId), accountId)));

        var update = Builders<Post>.Update
            .Push(x => x.Reactions, new Reaction(accountId, account.Name, account.Avatars.LastOrDefault()))
            .Inc(x => x.ReactionAmount.Value, 1);

        var result = await postCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        if(result.MatchedCount != 1)
            return Result.Fail(AppError.Conflict("Already reacted to this post"));

        await Task.WhenAll(
            _redisService.RemoveAsync(
                CacheKey.Post(postId),
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                $"{CollectionName.Post}:reaction",
                $"post:{postId}",
                cancellationToken));

        await InvalidatePostListsAsync(post.AccountId, post.ShopId, cancellationToken);

        if(post.AccountId == accountId)
            return Result.Ok();

        return await _notificationService.CreateAsync(
            post.AccountId,
            "New reaction",
            $"{account.Name.Value} reacted your post",
            NotificationType.PostReaction,
            postId,
            accountId,
            account.Avatars.LastOrDefault(),
            cancellationToken);
    }

    public async Task<Result> ProcessUnreactPostAsync(
        Guid accountId,
        Guid postId,
        CancellationToken cancellationToken)
    {
        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var filter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Eq(x => x.Id, postId),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Post>.Filter.AnyEq(x => x.Reactions.Select(r => r.AccountId), accountId));

        var update = Builders<Post>.Update
            .PullFilter(x => x.Reactions, r => r.AccountId == accountId)
            .Inc(x => x.ReactionAmount.Value, -1);

        var postOwnerProjection = Builders<Post>.Projection
            .Include(x => x.AccountId)
            .Include(x => x.ShopId);

        // FindOneAndUpdate rather than UpdateOne followed by a second Find: the author and
        // shop the invalidations below need come back in the same round trip as the pull,
        // the way DeleteCommentHandler already does it. The separate read also raced its own
        // write — anything that deleted the post in between left `post` null and made the
        // list invalidation a silent no-op.
        var post = await postCollection.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<Post>
            {
                ReturnDocument = ReturnDocument.After,
                Projection = postOwnerProjection
            },
            cancellationToken);

        if(post is null)
            return Result.Fail("Not reacted to this post");

        await Task.WhenAll(
            _redisService.RemoveAsync(
                CacheKey.Post(postId),
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                $"{CollectionName.Post}:reaction",
                $"post:{postId}",
                cancellationToken));

        await InvalidatePostListsAsync(post.AccountId, post.ShopId, cancellationToken);

        return Result.Ok();
    }

    private Task InvalidatePostListsAsync(Guid postAccountId, Guid shopId, CancellationToken cancellationToken)
    {
        return Task.WhenAll(
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                filterSignature: null,
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                $"account:{postAccountId}",
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                $"shop:{shopId}",
                cancellationToken));
    }
}
