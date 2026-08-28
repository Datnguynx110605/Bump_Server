using app;
using domain;
using FluentResults;
using MongoDB.Driver;

namespace infra;

internal sealed class ReactionService : IReactionService
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    public ReactionService(
        IMongoDbContext dbContext,
        IRedisService redisService)
    {
        _dbContext = dbContext;
        _redisService = redisService;
    }

    public async Task<Result> ProcessReactPostAsync(
        Guid accountId,
        Guid postId,
        CancellationToken cancellationToken)
    {
        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var accountProjection = Builders<Account>.Projection
            .Include(x => x.Name)
            .Include(x => x.Avatars.Last());

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

        await _redisService.RemoveAsync(
            CacheKey.Post(postId),
            cancellationToken);

        return Result.Ok();
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

        await _redisService.RemoveAsync(
            CacheKey.Post(postId),
            cancellationToken);

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
            .Include(x => x.Avatars.Last());

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

        await _redisService.RemoveAsync(
            CacheKey.Comment(commentId),
            cancellationToken);

        return Result.Ok();
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

        await _redisService.RemoveAsync(
            CacheKey.Comment(commentId),
            cancellationToken);

        return Result.Ok();
    }
}
