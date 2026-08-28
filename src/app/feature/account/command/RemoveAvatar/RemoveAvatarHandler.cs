using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class RemoveAvatarHandler
    : IRequestHandler<RemoveAvatarCommand, Result>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly ICurrentIdentityService _identityService;
    private readonly IR2Service _r2Service;
    public RemoveAvatarHandler(
        IMongoDbContext dbContext,
        IRedisService redisService,
        ICurrentIdentityService identityService,
        IR2Service r2Service)
    {
        _dbContext = dbContext;
        _redisService = redisService;
        _identityService = identityService;
        _r2Service = r2Service;
    }

    public async Task<Result> Handle(
        RemoveAvatarCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var exists = await accountCollection
            .Find(x => x.Id == identity.Value && !x.IsDeleted)
            .AnyAsync(cancellationToken);

        if(!exists)
            return Result.Fail("Unauthenticated");

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, identity.Value),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Account>.Filter.AnyEq(x => x.Avatars.Select(a => a.Url), request.url));

        var update = Builders<Account>.Update
            .PullFilter(x => x.Avatars, a => a.Url == request.url)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        var updateResult = await accountCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        if(updateResult.MatchedCount != 1)
            return Result.Fail("Avatar not found");

        await _r2Service.DeleteAsync(
            request.url,
            cancellationToken);

        string cacheKey = CacheKey.Account(identity.Value);

        await _redisService.RemoveAsync(
            cacheKey,
            cancellationToken);

        return Result.Ok();
    }
}
