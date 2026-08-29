using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class DeletePostHandler
    : IRequestHandler<DeletePostCommand, Result>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IR2Service _r2Service;
    private readonly IRedisService _redisService;
    private readonly ICurrentIdentityService _identityService;
    private readonly IRabbitMQService _rabbitMQService;
    public DeletePostHandler(
        IMongoDbContext dbContext,
        IR2Service r2Service,
        IRedisService redisService,
        ICurrentIdentityService identityService,
        IRabbitMQService rabbitMQService)
    {
        _dbContext = dbContext;
        _r2Service = r2Service;
        _redisService = redisService;
        _identityService = identityService;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result> Handle(
        DeletePostCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var projection = Builders<Post>.Projection
            .Include(x => x.ShopId)
            .Include(x => x.Images)
            .Include(x => x.IsDeleted);

        var post = await postCollection
            .Find(x => x.Id == request.postId && x.AccountId == identity.Value && !x.IsDeleted)
            .Project<Post>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(post is null)
            return Result.Fail("Post not found");

        var result = post.DeletePost();
        if(!result.IsSuccess)
            return Result.Fail(result.Error);

        var filter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Eq(x => x.Id, post.Id),
            Builders<Post>.Filter.Eq(x => x.AccountId, identity.Value),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false));

        var update = Builders<Post>.Update
            .Set(x => x.IsDeleted, post.IsDeleted)
            .Set(x => x.DeletedAt, post.DeletedAt);

        await postCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        foreach(var image in post.Images)
        {
            await _r2Service.DeleteAsync(
                image.Url,
                cancellationToken);
        }

        string cacheKey = CacheKey.Post(post.Id);

        await Task.WhenAll(
            _redisService.RemoveAsync(
                cacheKey,
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List(CollectionName.Post, filterSignature: null, new CursorPageRequest(null)),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List(CollectionName.Post, $"account:{identity.Value}", new CursorPageRequest(null)),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List(CollectionName.Post, $"shop:{post.ShopId}", new CursorPageRequest(null)),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List(CollectionName.Post, "mostReacted", new CursorPageRequest(null)),
                cancellationToken));

        var recalculateMessage = new RecalculateShopRateMessage(
            Guid.NewGuid(),
            post.ShopId);

        await _rabbitMQService.PublishAsync(
            QueueName.RecalculateShopRate,
            recalculateMessage,
            cancellationToken);

        return Result.Ok();
    }
}
