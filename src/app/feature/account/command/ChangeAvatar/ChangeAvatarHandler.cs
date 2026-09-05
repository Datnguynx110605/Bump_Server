using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class ChangeAvatarHandler
    : IRequestHandler<ChangeAvatarCommand, Result>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly IR2Service _r2Service;
    private readonly ICurrentIdentityService _identityService;
    public ChangeAvatarHandler(
        IMongoDbContext dbContext,
        IR2Service r2Service,
        IRedisService redisService,
        ICurrentIdentityService identityService)
    {
        _dbContext = dbContext;
        _r2Service = r2Service;
        _redisService = redisService;
        _identityService = identityService;
    }

    public async Task<Result> Handle(
        ChangeAvatarCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var projection = Builders<Account>.Projection
            .Include(x => x.Avatars)
            .Include(x => x.IsDeleted);

        var account = await accountCollection
            .Find(x => x.Id == identity.Value && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail(AppError.Unauthorized("Unauthenticated"));

        var url = await _r2Service.UploadAsync(
            request.avatars,
            request.fileName,
            cancellationToken);

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, account.Id),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Account>.Filter.Not(Builders<Account>.Filter.AnyEq(x => x.Avatars.Select(r => r.Url), url)));

        var update = Builders<Account>.Update
            .Push(x => x.Avatars, new Avatar(url))
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        var updateResult = await accountCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        if(updateResult.MatchedCount != 1)
            return Result.Fail(AppError.Conflict("New avatar must be different"));

        string cacheKey = CacheKey.Account(account.Id);

        await _redisService.RemoveAsync(
            cacheKey,
            cancellationToken);

        return Result.Ok();
    }
}
