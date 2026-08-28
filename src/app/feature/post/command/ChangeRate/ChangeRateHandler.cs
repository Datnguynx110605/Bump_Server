using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class ChangeRateHandler
    : IRequestHandler<ChangeRateCommand, Result>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly ICurrentIdentityService _identityService;
    public ChangeRateHandler(
        IMongoDbContext dbContext,
        IRedisService redisService,
        ICurrentIdentityService identityService)
    {
        _dbContext = dbContext;
        _redisService = redisService;
        _identityService = identityService;
    }

    public async Task<Result> Handle(
        ChangeRateCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var projection = Builders<Post>.Projection
            .Include(x => x.Rate)
            .Include(x => x.IsDeleted);

        var post = await postCollection
            .Find(x => x.Id == request.postId && x.AccountId == identity.Value && !x.IsDeleted)
            .Project<Post>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(post is null)
            return Result.Fail("Post not found");

        var result = post.ChangeRate(request.rate);
        if(!result.IsSuccess)
            return Result.Fail(result.Error);

        var filter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Eq(x => x.Id, post.Id),
            Builders<Post>.Filter.Eq(x => x.AccountId, identity.Value),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false));

        var update = Builders<Post>.Update
            .Set(x => x.Rate, post.Rate)
            .Set(x => x.UpdatedAt, post.UpdatedAt);

        await postCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        string cacheKey = CacheKey.Post(post.Id);

        await _redisService.RemoveAsync(
            cacheKey,
            cancellationToken);

        return Result.Ok();
    }
}
