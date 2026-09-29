using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class DeleteShopHandler
    : IRequestHandler<DeleteShopCommand, Result>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly ICurrentIdentityService _identityService;
    public DeleteShopHandler(
        IMongoDbContext dbContext,
        IRedisService redisService,
        ICurrentIdentityService identityService)
    {
        _dbContext = dbContext;
        _redisService = redisService;
        _identityService = identityService;
    }

    public async Task<Result> Handle(
        DeleteShopCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        var shopCollection = _dbContext.GetCollection<Shop>(CollectionName.Shop);

        var projection = Builders<Shop>.Projection
            .Include(x => x.IsDeleted);

        var shop = await shopCollection
            .Find(x => x.Id == request.shopId && !x.IsDeleted)
            .Project<Shop>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(shop is null)
            return Result.Fail(AppError.NotFound("Shop not found"));

        // Refused rather than cascaded. A shop delete used to flip one flag and stop, which
        // left every post still pointing at it: GET /post/shop/{id} kept serving them, saved
        // shops kept referencing it, and because the unique index on PlaceId is scoped to
        // live rows, the same shop could immediately be created again — leaving the old
        // posts attached to a row nothing can reach.
        //
        // Refusing instead of cascading because the posts are other people's content and an
        // administrator deleting a duplicate shop entry is not asking to destroy the reviews
        // attached to it. Deleting those first is an explicit act.
        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        bool hasPosts = await postCollection
            .Find(x => x.ShopId == request.shopId && !x.IsDeleted)
            .AnyAsync(cancellationToken);

        if(hasPosts)
            return Result.Fail(AppError.Conflict(
                "Shop still has posts. Delete them before deleting the shop."));

        var result = shop.DeleteShop();
        if(!result.IsSuccess)
            return Result.Fail(result.Error);

        var filter = Builders<Shop>.Filter.And(
            Builders<Shop>.Filter.Eq(x => x.Id, shop.Id),
            Builders<Shop>.Filter.Eq(x => x.IsDeleted, false));

        var update = Builders<Shop>.Update
            .Set(x => x.IsDeleted, shop.IsDeleted)
            .Set(x => x.DeletedAt, shop.DeletedAt);

        await shopCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        string cacheKey = CacheKey.Shop(shop.Id);

        await Task.WhenAll(
            _redisService.RemoveAsync(
                cacheKey,
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Shop,
                filterSignature: null,
                cancellationToken));

        return Result.Ok();
    }
}
