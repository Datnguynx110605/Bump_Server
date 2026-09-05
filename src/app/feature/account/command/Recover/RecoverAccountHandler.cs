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
    private readonly IRegistrationService _registrationService;
    public RecoverAccountHandler(
        IMongoDbContext dbContext,
        IRedisService redisService,
        IRegistrationService registrationService)
    {
        _dbContext = dbContext;
        _redisService = redisService;
        _registrationService = registrationService;
    }

    public async Task<Result> Handle(
        RecoverAccountCommand request,
        CancellationToken cancellationToken)
    {
        var rawEmail = await _registrationService.ExtractEmailAsync(
            request.registrationToken,
            cancellationToken);
        if(rawEmail is null)
            return Result.Fail(AppError.Unauthorized("Unauthenticated"));

        var emailResult = Email.Create(rawEmail);
        if(!emailResult.IsSuccess)
            return Result.Fail(emailResult.Error);

        Email email = emailResult.Value;

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var projection = Builders<Account>.Projection
            .Include(x => x.IsDeleted);

        var account = await accountCollection
            .Find(x => x.Email == email && x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail(AppError.NotFound("Account not found"));

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
