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
    private readonly IBcryptService _bcryptService;
    public ChangeEmailHandler(
        IRegistrationService registrationService,
        IMongoDbContext dbContext,
        IRedisService redisService,
        ICurrentIdentityService identityService,
        IBcryptService bcryptService)
    {
        _registrationService = registrationService;
        _dbContext = dbContext;
        _redisService = redisService;
        _identityService = identityService;
        _bcryptService = bcryptService;
    }

    public async Task<Result> Handle(
        ChangeEmailCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

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
            .Include(x => x.Email)
            .Include(x => x.Password)
            .Include(x => x.IsDeleted);

        var account = await accountCollection
            .Find(x => x.Id == identity.Value && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail(AppError.Unauthorized("Unauthenticated"));

        // The access token alone is not enough to move the address the account recovers
        // through. Without this check a stolen token was a permanent takeover: mint a
        // registration token for an address you control, point the account at it, then walk
        // the ordinary reset-password flow and the real owner has no way back in. The same
        // reason ChangePasswordHandler asks for the current password before it touches
        // credentials — this endpoint decides who can prove ownership later, so it is a
        // credential change in everything but name.
        if(!_bcryptService.VerifyPassword(request.currentPassword, account.Password))
            return Result.Fail(AppError.Unauthorized("Current password is incorrect"));

        Email oldEmail = account.Email;

        var result = account.ChangeEmail(email);
        if(!result.IsSuccess)
            return Result.Fail(AppError.Conflict(result.Error!));

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, account.Id),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false));

        var update = Builders<Account>.Update
            .Set(x => x.Email, account.Email)
            .Set(x => x.UpdatedAt, account.UpdatedAt);

        try
        {
            await accountCollection.UpdateOneAsync(
                filter,
                update,
                cancellationToken : cancellationToken);
        }
        catch(MongoWriteException ex)
            when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // The partial unique index on Email rejects a move onto an address some other
            // live account already holds. Translated here for the same reason
            // CreateAccountHandler translates it: unhandled, the driver's exception reaches
            // UseExceptionHandler and the caller reads 500 for what is an ordinary conflict.
            return Result.Fail(AppError.Conflict("Email already exists"));
        }

        await _registrationService.ConsumeTokenAsync(
            request.registrationToken,
            cancellationToken);

        await Task.WhenAll(
            _redisService.RemoveAsync(
                CacheKey.Account(account.Id),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.AccountByEmail(oldEmail),
                cancellationToken));

        return Result.Ok();
    }
}
