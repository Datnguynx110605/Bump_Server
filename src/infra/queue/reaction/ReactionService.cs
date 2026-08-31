using app;
using domain;
using FluentResults;
using MongoDB.Driver;

namespace infra;

public sealed class ReactionService : IReactionService
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly INotificationService _notificationService;
    public ReactionService(
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

        var account = await accountCollection
            .Find(x => x.Id == accountId && !x.IsDeleted)
            .Project<Account>(accountProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail("Account not found");

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

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
            return Result.Fail("Already reacted to this post");

        await Task.WhenAll(
            _redisService.RemoveAsync(
                CacheKey.Post(postId),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List($"{CollectionName.Post}:reaction", $"post:{postId}", new CursorPageRequest(null)),
                cancellationToken));

        var postOwnerProjection = Builders<Post>.Projection
            .Include(x => x.AccountId)
            .Include(x => x.ShopId);

        var post = await postCollection
            .Find(x => x.Id == postId && !x.IsDeleted)
            .Project<Post>(postOwnerProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if(post is null)
            return Result.Fail("Post not found");

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

        var result = await postCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        if(result.MatchedCount != 1)
            return Result.Fail("Not reacted to this post");

        await Task.WhenAll(
            _redisService.RemoveAsync(
                CacheKey.Post(postId),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List($"{CollectionName.Post}:reaction", $"post:{postId}", new CursorPageRequest(null)),
                cancellationToken));

        var postOwnerProjection = Builders<Post>.Projection
            .Include(x => x.AccountId)
            .Include(x => x.ShopId);

        var post = await postCollection
            .Find(x => x.Id == postId && !x.IsDeleted)
            .Project<Post>(postOwnerProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if(post is not null)
            await InvalidatePostListsAsync(post.AccountId, post.ShopId, cancellationToken);

        return Result.Ok();
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
            return Result.Fail("Account not found");

        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

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
            return Result.Fail("Already reacted to this comment");

        await Task.WhenAll(
            _redisService.RemoveAsync(
                CacheKey.Comment(commentId),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List($"{CollectionName.Comment}:reaction", $"comment:{commentId}", new CursorPageRequest(null)),
                cancellationToken));

        var commentOwnerProjection = Builders<Comment>.Projection
            .Include(x => x.AccountId)
            .Include(x => x.PostId);

        var comment = await commentCollection
            .Find(x => x.Id == commentId && !x.IsDeleted)
            .Project<Comment>(commentOwnerProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if(comment is null)
            return Result.Fail("Comment not found");

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
            _redisService.RemoveAsync(
                CacheKey.List($"{CollectionName.Comment}:reaction", $"comment:{commentId}", new CursorPageRequest(null)),
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

    private Task InvalidatePostListsAsync(Guid postAccountId, Guid shopId, CancellationToken cancellationToken)
    {
        return Task.WhenAll(
            _redisService.RemoveAsync(
                CacheKey.List(CollectionName.Post, filterSignature: null, new CursorPageRequest(null)),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List(CollectionName.Post, $"account:{postAccountId}", new CursorPageRequest(null)),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List(CollectionName.Post, $"shop:{shopId}", new CursorPageRequest(null)),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List(CollectionName.Post, "mostReacted", new CursorPageRequest(null)),
                cancellationToken));
    }

    private Task InvalidateCommentListsAsync(Guid postId, Guid commentAccountId, CancellationToken cancellationToken)
    {
        return Task.WhenAll(
            _redisService.RemoveAsync(
                CacheKey.List(CollectionName.Comment, $"post:{postId}", new CursorPageRequest(null)),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List(CollectionName.Comment, $"account:{commentAccountId}", new CursorPageRequest(null)),
                cancellationToken));
    }
}
