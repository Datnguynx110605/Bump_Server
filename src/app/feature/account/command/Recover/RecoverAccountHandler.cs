using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class RecoverAccountHandler
    : IRequestHandler<RecoverAccountCommand, Result>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly ICurrentIdentityService _identityService;
    public RecoverAccountHandler(
        IMongoDbContext dbContext,
        IRedisService redisService,
        ICurrentIdentityService identityService)
    {
        _dbContext = dbContext;
        _redisService = redisService;
        _identityService = identityService;
    }

    public async Task<Result> Handle(
        RecoverAccountCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var projection = Builders<Account>.Projection
            .Include(x => x.IsDeleted);

        var account = await accountCollection
            .Find(x => x.Id == identity.Value && x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail("Account not found");

        var result = account.RecoverAccount();
        if(!result.IsSuccess)
            return Result.Fail(result.Error);

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, account.Id),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, true));

        var update = Builders<Account>.Update
            .Set(x => x.IsDeleted, account.IsDeleted)
            .Set(x => x.DeletedAt, account.DeletedAt)
            .Set(x => x.RecoveredAt, account.RecoveredAt);

        await accountCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        string cacheKey = CacheKey.Account(account.Id);

        await _redisService.RemoveAsync(
            cacheKey,
            cancellationToken);

        return Result.Ok();
    }
}
