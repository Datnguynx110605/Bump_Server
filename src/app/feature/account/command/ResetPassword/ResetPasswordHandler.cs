using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class ResetPasswordHandler
    : IRequestHandler<ResetPasswordCommand, Result>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly IRegistrationService _registrationService;
    private readonly IBcryptService _bcryptService;
    public ResetPasswordHandler(
        IMongoDbContext dbContext,
        IRedisService redisService,
        IRegistrationService registrationService,
        IBcryptService bcryptService)
    {
        _dbContext = dbContext;
        _redisService = redisService;
        _registrationService = registrationService;
        _bcryptService = bcryptService;
    }

    public async Task<Result> Handle(
        ResetPasswordCommand request,
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
            .Include(x => x.Password)
            .Include(x => x.IsDeleted);

        var account = await accountCollection
            .Find(x => x.Email == email && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail(AppError.NotFound("Account not found"));

        string hashPassword = _bcryptService.HashPassword(request.newPassword);

        var result = account.ChangePassword(hashPassword);
        if(!result.IsSuccess)
            return Result.Fail(AppError.Conflict(result.Error!));

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, account.Id),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false));

        var update = Builders<Account>.Update
            .Set(x => x.Password, account.Password)
            .Set(x => x.UpdatedAt, account.UpdatedAt);

        await accountCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        await _registrationService.ConsumeTokenAsync(
            request.registrationToken,
            cancellationToken);

        string cacheKey = CacheKey.Account(account.Id);

        await _redisService.RemoveAsync(
            cacheKey,
            cancellationToken);

        return Result.Ok();
    }
}
