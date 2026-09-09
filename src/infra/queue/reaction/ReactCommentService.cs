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

        var account = await accountCollection
            .Find(x => x.Id == accountId && !x.IsDeleted)
            .Project<Account>(accountProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail(AppError.NotFound("Account not found"));

        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        // Read before the write rather than after it. The author id is what the guard
        // compares against, and this read already existed below for the notification, so
        // moving it up costs nothing.
        var commentOwnerProjection = Builders<Comment>.Projection
            .Include(x => x.AccountId)
            .Include(x => x.PostId);

        var comment = await commentCollection
            .Find(x => x.Id == commentId && !x.IsDeleted)
            .Project<Comment>(commentOwnerProjection)
            .FirstOrDefaultAsync(cancellationToken);

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

        var result = await commentCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        if(result.MatchedCount != 1)
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

        var commentOwnerProjection = Builders<Comment>.Projection
            .Include(x => x.AccountId)
            .Include(x => x.PostId);

        var comment = await commentCollection
            .Find(x => x.Id == commentId && !x.IsDeleted)
            .Project<Comment>(commentOwnerProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if(comment is not null)
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
