using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class ChangeEmailHandler
    : IRequestHandler<ChangeEmailCommand, Result>
{
    private readonly IRegistrationService _registrationService;
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly ICurrentIdentityService _identityService;
    public ChangeEmailHandler(
        IRegistrationService registrationService,
        IMongoDbContext dbContext,
        IRedisService redisService,
        ICurrentIdentityService identityService)
    {
        _registrationService = registrationService;
        _dbContext = dbContext;
        _redisService = redisService;
        _identityService = identityService;
    }

    public async Task<Result> Handle(
        ChangeEmailCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

        var rawEmail = await _registrationService.ExtractEmailAsync(
            request.registrationToken,
            cancellationToken
        );
        if(rawEmail is null)
            return Result.Fail("Unauthenticated");

        var emailResult = Email.Create(rawEmail);
        if(!emailResult.IsSuccess)
            return Result.Fail(emailResult.Error);

        Email email = emailResult.Value;

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var projection = Builders<Account>.Projection
            .Include(x => x.Email)
            .Include(x => x.IsDeleted);

        var account = await accountCollection
            .Find(x => x.Id == identity.Value && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail("Unauthenticated");

        Email oldEmail = account.Email;

        var result = account.ChangeEmail(email);
        if(!result.IsSuccess)
            return Result.Fail(result.Error);

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, account.Id),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false));

        var update = Builders<Account>.Update
            .Set(x => x.Email, account.Email)
            .Set(x => x.UpdatedAt, account.UpdatedAt);

        await accountCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        await _registrationService.InvalidateTokenAsync(
            request.registrationToken,
            cancellationToken);

        await _redisService.RemoveAsync(
            CacheKey.Account(account.Id),
            cancellationToken);

        await _redisService.RemoveAsync(
            CacheKey.AccountByEmail(oldEmail),
            cancellationToken);

        return Result.Ok();
    }
}
