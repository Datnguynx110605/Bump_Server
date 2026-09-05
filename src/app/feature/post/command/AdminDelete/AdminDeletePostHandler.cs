using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

// Mirrors DeletePostHandler without the ownership filter, rather than adding a bypass
// flag to it: the owner path is what the client depends on and must keep its filter.
// Every side effect is replicated — image cleanup, cache invalidation, the delete-post
// cascade and the shop rate recalculation — because a delete that only flips IsDeleted
// leaves shop rates and comment rows stale.
internal sealed class AdminDeletePostHandler
    : IRequestHandler<AdminDeletePostCommand, Result>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IR2Service _r2Service;
    private readonly IRedisService _redisService;
    private readonly IRabbitMQService _rabbitMQService;
    public AdminDeletePostHandler(
        IMongoDbContext dbContext,
        IR2Service r2Service,
        IRedisService redisService,
        IRabbitMQService rabbitMQService)
    {
        _dbContext = dbContext;
        _r2Service = r2Service;
        _redisService = redisService;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result> Handle(
        AdminDeletePostCommand request,
        CancellationToken cancellationToken)
    {
        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var projection = Builders<Post>.Projection
            .Include(x => x.AccountId)
            .Include(x => x.ShopId)
            .Include(x => x.Images)
            .Include(x => x.IsDeleted);

        var post = await postCollection
            .Find(x => x.Id == request.postId && !x.IsDeleted)
            .Project<Post>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(post is null)
            return Result.Fail(AppError.NotFound("Post not found"));

        var result = post.DeletePost();
        if(!result.IsSuccess)
            return Result.Fail(result.Error);

        var filter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Eq(x => x.Id, post.Id),
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

        // The per-account list signature is the post author's id, not the acting
        // administrator's — invalidating the admin's own list would leave the
        // author's cached page serving a deleted post.
        await Task.WhenAll(
            _redisService.RemoveAsync(
                cacheKey,
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                filterSignature: null,
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                $"account:{post.AccountId}",
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                $"shop:{post.ShopId}",
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                "mostReacted",
                cancellationToken));

        var recalculateMessage = new RecalculateShopRateMessage(
            Guid.NewGuid(),
            post.ShopId);

        var deletePostMessage = new PostMessage(
            Guid.NewGuid(),
            post.Id);

        await _rabbitMQService.PublishAsync(
            QueueName.RecalculateShopRate,
            recalculateMessage,
            cancellationToken);
        await _rabbitMQService.PublishAsync(
            QueueName.DeletePost,
            deletePostMessage,
            cancellationToken);

        return Result.Ok();
    }
}
