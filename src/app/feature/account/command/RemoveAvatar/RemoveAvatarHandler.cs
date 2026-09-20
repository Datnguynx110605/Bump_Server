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
    private readonly IRabbitMQService _rabbitMQService;
    public RemoveAvatarHandler(
        IMongoDbContext dbContext,
        IRedisService redisService,
        ICurrentIdentityService identityService,
        IR2Service r2Service,
        IRabbitMQService rabbitMQService)
    {
        _dbContext = dbContext;
        _redisService = redisService;
        _identityService = identityService;
        _r2Service = r2Service;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result> Handle(
        RemoveAvatarCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var exists = await accountCollection
            .Find(x => x.Id == identity.Value && !x.IsDeleted)
            .AnyAsync(cancellationToken);

        if(!exists)
            return Result.Fail(AppError.Unauthorized("Unauthenticated"));

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

        // The match is what authorizes the R2 delete below: request.url is caller-supplied,
        // and only a URL already sitting in this account's own Avatars array gets past it.
        if(updateResult.MatchedCount != 1)
            return Result.Fail(AppError.NotFound("Avatar not found"));

        await _r2Service.DeleteAsync(
            request.url,
            cancellationToken);

        string cacheKey = CacheKey.Account(identity.Value);

        await _redisService.RemoveAsync(
            cacheKey,
            cancellationToken);

        // Clearing the avatar has to reach the denormalized copies too, or the removed
        // image keeps rendering from every follower, block and reaction snapshot that still
        // holds it. The worker reads the account's current avatar, which is now absent, and
        // writes null into those copies.
        await _rabbitMQService.PublishAsync(
            QueueName.ProfileChanged,
            new ProfileMessage(Guid.NewGuid(), identity.Value),
            cancellationToken);

        return Result.Ok();
    }
}
