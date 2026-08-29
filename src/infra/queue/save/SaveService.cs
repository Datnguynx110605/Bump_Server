using app;
using domain;
using FluentResults;
using MongoDB.Driver;

namespace infra;

public sealed class SaveService : ISaveService
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    public SaveService(
        IMongoDbContext dbContext,
        IRedisService redisService)
    {
        _dbContext = dbContext;
        _redisService = redisService;
    }

    public async Task<Result> ProcessSavePostAsync(
        Guid accountId,
        Guid postId,
        CancellationToken cancellationToken)
    {
        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var post = await postCollection
            .Find(x => x.Id == postId && !x.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if(post is null)
            return Result.Fail("Post not found");

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, accountId),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Account>.Filter.Not(Builders<Account>.Filter.AnyEq(x => x.SavePosts.Select(sp => sp.PostId), postId)));

        var update = Builders<Account>.Update.Push(x => x.SavePosts, new SavedPost(postId));

        var result = await accountCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        if(result.MatchedCount != 1)
            return Result.Fail("Already saved this post");

        await InvalidateSavedPostListAsync(accountId, cancellationToken);

        return Result.Ok();
    }

    public async Task<Result> ProcessUnsavePostAsync(
        Guid accountId,
        Guid postId,
        CancellationToken cancellationToken)
    {
        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, accountId),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Account>.Filter.AnyEq(x => x.SavePosts.Select(sp => sp.PostId), postId));

        var update = Builders<Account>.Update.PullFilter(x => x.SavePosts, sp => sp.PostId == postId);

        var result = await accountCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        if(result.MatchedCount != 1)
            return Result.Fail("Not saved this post");

        await InvalidateSavedPostListAsync(accountId, cancellationToken);

        return Result.Ok();
    }

    public async Task<Result> ProcessSaveShopAsync(
        Guid accountId,
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var shopCollection = _dbContext.GetCollection<Shop>(CollectionName.Shop);

        var shop = await shopCollection
            .Find(x => x.Id == shopId && !x.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if(shop is null)
            return Result.Fail("Shop not found");

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, accountId),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Account>.Filter.Not(Builders<Account>.Filter.AnyEq(x => x.SaveShops.Select(ss => ss.ShopId), shopId)));

        var update = Builders<Account>.Update.Push(x => x.SaveShops, new SavedShop(shopId));

        var result = await accountCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        if(result.MatchedCount != 1)
            return Result.Fail("Already saved this shop");

        await InvalidateSavedShopListAsync(accountId, cancellationToken);

        return Result.Ok();
    }

    public async Task<Result> ProcessUnsaveShopAsync(
        Guid accountId,
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, accountId),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Account>.Filter.AnyEq(x => x.SaveShops.Select(ss => ss.ShopId), shopId));

        var update = Builders<Account>.Update.PullFilter(x => x.SaveShops, ss => ss.ShopId == shopId);

        var result = await accountCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        if(result.MatchedCount != 1)
            return Result.Fail("Not saved this shop");

        await InvalidateSavedShopListAsync(accountId, cancellationToken);

        return Result.Ok();
    }

    private Task InvalidateSavedPostListAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return _redisService.RemoveAsync(
            CacheKey.List($"{CollectionName.Account}:savepost", $"account:{accountId}", new CursorPageRequest(null)),
            cancellationToken);
    }

    private Task InvalidateSavedShopListAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return _redisService.RemoveAsync(
            CacheKey.List($"{CollectionName.Account}:saveshop", $"account:{accountId}", new CursorPageRequest(null)),
            cancellationToken);
    }
}
