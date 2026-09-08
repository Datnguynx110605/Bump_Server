using app;
using domain;
using FluentResults;
using MongoDB.Driver;

namespace infra;

public sealed class ShopRateService : IShopRateService
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    public ShopRateService(
        IMongoDbContext dbContext,
        IRedisService redisService)
    {
        _dbContext = dbContext;
        _redisService = redisService;
    }

    public async Task<Result> ProcessRecalculateShopRateAsync(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var aggregation = await postCollection.Aggregate()
            .Match(x => x.ShopId == shopId && !x.IsDeleted)
            .Group(x => x.ShopId, g => new { Average = g.Average(x => x.Rate.Value) })
            .FirstOrDefaultAsync(cancellationToken);

        var averagedRate = aggregation is null
            ? 0
            : (int)Math.Round(aggregation.Average, MidpointRounding.AwayFromZero);

        var rateResult = Amount.Create(averagedRate);
        if(!rateResult.IsSuccess)
            return Result.Fail(rateResult.Error);

        var shopCollection = _dbContext.GetCollection<Shop>(CollectionName.Shop);

        var projection = Builders<Shop>.Projection
            .Include(x => x.Rate)
            .Include(x => x.IsDeleted);

        var shop = await shopCollection
            .Find(x => x.Id == shopId && !x.IsDeleted)
            .Project<Shop>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(shop is null)
            return Result.Fail(AppError.NotFound("Shop not found"));

        var result = shop.ChangeRate(rateResult.Value);
        if(!result.IsSuccess)
            return Result.Fail(result.Error);

        var filter = Builders<Shop>.Filter.And(
            Builders<Shop>.Filter.Eq(x => x.Id, shop.Id),
            Builders<Shop>.Filter.Eq(x => x.IsDeleted, false));

        var update = Builders<Shop>.Update
            .Set(x => x.Rate, shop.Rate)
            .Set(x => x.UpdatedAt, shop.UpdatedAt);

        await shopCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        await Task.WhenAll(
            _redisService.RemoveAsync(
                CacheKey.Shop(shop.Id),
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Shop,
                filterSignature: null,
                cancellationToken));

        return Result.Ok();
    }
}
