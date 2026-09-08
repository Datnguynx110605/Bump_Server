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

        var account = await accountCollection
            .Find(x => x.Id == accountId && !x.IsDeleted)
            .Project<Account>(accountProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail(AppError.NotFound("Account not found"));

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

        var postOwnerProjection = Builders<Post>.Projection
            .Include(x => x.AccountId)
            .Include(x => x.ShopId);

        var post = await postCollection
            .Find(x => x.Id == postId && !x.IsDeleted)
            .Project<Post>(postOwnerProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if(post is null)
            return Result.Fail(AppError.NotFound("Post not found"));

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
            ListCache.InvalidateAsync(
                _redisService,
                $"{CollectionName.Post}:reaction",
                $"post:{postId}",
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
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                "mostReacted",
                cancellationToken));
    }
}
