using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class ChangeCaptionHandler
    : IRequestHandler<ChangeCaptionCommand, Result>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly ICurrentIdentityService _identityService;
    public ChangeCaptionHandler(
        IMongoDbContext dbContext,
        IRedisService redisService,
        ICurrentIdentityService identityService)
    {
        _dbContext = dbContext;
        _redisService = redisService;
        _identityService = identityService;
    }

    public async Task<Result> Handle(
        ChangeCaptionCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var projection = Builders<Post>.Projection
            .Include(x => x.Caption)
            .Include(x => x.ShopId)
            .Include(x => x.IsDeleted);

        var post = await postCollection
            .Find(x => x.Id == request.postId && x.AccountId == identity.Value && !x.IsDeleted)
            .Project<Post>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(post is null)
            return Result.Fail("Post not found");

        var result = post.ChangeCaption(request.caption);
        if(!result.IsSuccess)
            return Result.Fail(result.Error);

        var filter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Eq(x => x.Id, post.Id),
            Builders<Post>.Filter.Eq(x => x.AccountId, identity.Value),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false));

        var update = Builders<Post>.Update
            .Set(x => x.Caption, post.Caption)
            .Set(x => x.UpdatedAt, post.UpdatedAt);

        await postCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

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

        return Result.Ok();
    }
}
