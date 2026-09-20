using app;
using domain;
using FluentResults;
using MongoDB.Driver;

namespace infra;

public sealed class ReactCommentService : IReactCommentService
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly INotificationService _notificationService;
    public ReactCommentService(
        IMongoDbContext dbContext,
        IRedisService redisService,
        INotificationService notificationService)
    {
        _dbContext = dbContext;
        _redisService = redisService;
        _notificationService = notificationService;
    }

    public async Task<Result> ProcessReactCommentAsync(
        Guid accountId,
        Guid commentId,
        CancellationToken cancellationToken)
    {
        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var accountProjection = Builders<Account>.Projection
            .Include(x => x.Name)
            .Include(x => x.Avatars);

        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        // Read before the write rather than after it. The author id is what the guard
        // compares against, and this read already existed below for the notification, so
        // moving it up costs nothing.
        var commentOwnerProjection = Builders<Comment>.Projection
            .Include(x => x.AccountId)
            .Include(x => x.PostId);

        // Started together: different collections, neither needs the other's answer. Only
        // the block guard below depends on both, so overlapping these takes the path from
        // four sequential round trips to three.
        var accountTask = accountCollection
            .Find(x => x.Id == accountId && !x.IsDeleted)
            .Project<Account>(accountProjection)
            .FirstOrDefaultAsync(cancellationToken);

        var commentTask = commentCollection
            .Find(x => x.Id == commentId && !x.IsDeleted)
            .Project<Comment>(commentOwnerProjection)
            .FirstOrDefaultAsync(cancellationToken);

        await Task.WhenAll(accountTask, commentTask);

        var account = accountTask.Result;
        var comment = commentTask.Result;

        if(account is null)
            return Result.Fail(AppError.NotFound("Account not found"));

        if(comment is null)
            return Result.Fail(AppError.NotFound("Comment not found"));

        // Silently refused, not reported: the endpoint already answered 202, and an error
        // here would tell the actor that the other account blocked them. A failed Result is
        // logged by QueueConsumerHost and the message is not retried.
        if(await BlockGuard.IsBlockedAsync(accountCollection, accountId, comment.AccountId, cancellationToken))
            return Result.Fail("Blocked by or blocking this account");

        var filter = Builders<Comment>.Filter.And(
            Builders<Comment>.Filter.Eq(x => x.Id, commentId),
            Builders<Comment>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Comment>.Filter.Not(Builders<Comment>.Filter.AnyEq(x => x.Reactions.Select(r => r.AccountId), accountId)));

        var update = Builders<Comment>.Update
            .Push(x => x.Reactions, new Reaction(accountId, account.Name, account.Avatars.LastOrDefault()))
            .Inc(x => x.ReactionAmount.Value, 1);

        var result = await commentCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        if(result.MatchedCount != 1)
            return Result.Fail(AppError.Conflict("Already reacted to this comment"));

        await Task.WhenAll(
            _redisService.RemoveAsync(
                CacheKey.Comment(commentId),
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                $"{CollectionName.Comment}:reaction",
                $"comment:{commentId}",
                cancellationToken));

        await InvalidateCommentListsAsync(comment.PostId, comment.AccountId, cancellationToken);

        if(comment.AccountId == accountId)
            return Result.Ok();

        return await _notificationService.CreateAsync(
            comment.AccountId,
            "New reaction",
            $"{account.Name.Value} reacted your comment",
            NotificationType.CommentReaction,
            comment.PostId,
            accountId,
            account.Avatars.LastOrDefault(),
            cancellationToken);
    }

    public async Task<Result> ProcessUnreactCommentAsync(
        Guid accountId,
        Guid commentId,
        CancellationToken cancellationToken)
    {
        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        var filter = Builders<Comment>.Filter.And(
            Builders<Comment>.Filter.Eq(x => x.Id, commentId),
            Builders<Comment>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Comment>.Filter.AnyEq(x => x.Reactions.Select(r => r.AccountId), accountId));

        var update = Builders<Comment>.Update
            .PullFilter(x => x.Reactions, r => r.AccountId == accountId)
            .Inc(x => x.ReactionAmount.Value, -1);

        var commentOwnerProjection = Builders<Comment>.Projection
            .Include(x => x.AccountId)
            .Include(x => x.PostId);

        // FindOneAndUpdate rather than UpdateOne followed by a second Find: the post and
        // author the invalidations below need come back in the same round trip as the pull,
        // the way DeleteCommentHandler already does it. The separate read also raced its own
        // write — anything that deleted the comment in between left `comment` null and made
        // the list invalidation a silent no-op.
        var comment = await commentCollection.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<Comment>
            {
                ReturnDocument = ReturnDocument.After,
                Projection = commentOwnerProjection
            },
            cancellationToken);

        if(comment is null)
            return Result.Fail("Not reacted to this comment");

        await Task.WhenAll(
            _redisService.RemoveAsync(
                CacheKey.Comment(commentId),
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                $"{CollectionName.Comment}:reaction",
                $"comment:{commentId}",
                cancellationToken));

        await InvalidateCommentListsAsync(comment.PostId, comment.AccountId, cancellationToken);

        return Result.Ok();
    }

    private Task InvalidateCommentListsAsync(Guid postId, Guid commentAccountId, CancellationToken cancellationToken)
    {
        return Task.WhenAll(
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Comment,
                $"post:{postId}",
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Comment,
                $"account:{commentAccountId}",
                cancellationToken));
    }
}
