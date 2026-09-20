using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class ChangePasswordHandler
    : IRequestHandler<ChangePasswordCommand, Result<AuthResult>>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly ICurrentIdentityService _identityService;
    private readonly IBcryptService _bcryptService;
    private readonly IJwtService _jwtService;
    public ChangePasswordHandler(
        IMongoDbContext dbContext,
        IRedisService redisService,
        ICurrentIdentityService identityService,
        IBcryptService bcryptService,
        IJwtService jwtService)
    {
        _dbContext = dbContext;
        _redisService = redisService;
        _identityService = identityService;
        _bcryptService = bcryptService;
        _jwtService = jwtService;
    }

    public async Task<Result<AuthResult>> Handle(
        ChangePasswordCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var projection = Builders<Account>.Projection
            .Include(x => x.Password)
            .Include(x => x.Role)
            .Include(x => x.IsDeleted);

        var account = await accountCollection
            .Find(x => x.Id == identity.Value && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail(AppError.Unauthorized("Unauthenticated"));

        if(!_bcryptService.VerifyPassword(request.currentPassword, account.Password))
            return Result.Fail("Current password is incorrect");

        // Compared against the stored hash through BCrypt, not by hashing the new password
        // and comparing the two hashes: BCrypt salts randomly, so two hashes of the same
        // plaintext never match and the rule that used to live on the entity never fired.
        if(_bcryptService.VerifyPassword(request.newPassword, account.Password))
            return Result.Fail(AppError.Conflict("New password must be different"));

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

        // Every session on the account dies with the old password. Without this a stolen
        // refresh token outlives the change that was meant to evict it, for as long as the
        // token's own 7-day lifetime.
        var refreshTokenCollection = _dbContext.GetCollection<RefreshToken>(CollectionName.RefreshToken);

        // Deleted, not marked revoked. A revoked row is the signal RequestNewAccessToken uses
        // to detect a replayed token and cut every session on the account — and a credential
        // change is not a replay. Marking them left a second device holding a row that looked
        // exactly like a stolen one, so its next refresh nuked the session the user had just
        // created here. These rows are worthless either way, and SoftDeletePurgeHost already
        // hard-deletes revoked tokens on sight, so deleting now only brings that forward.
        await refreshTokenCollection.DeleteManyAsync(
            Builders<RefreshToken>.Filter.And(
                Builders<RefreshToken>.Filter.Eq(x => x.AccountId, account.Id),
                Builders<RefreshToken>.Filter.Eq(x => x.IsRevoked, false)),
            cancellationToken);

        // A credential reset means some device is no longer trusted, and a device that can
        // no longer authenticate should not keep receiving notification text on its lock
        // screen. The legitimate device re-registers on its next launch.
        var deviceTokenCollection = _dbContext.GetCollection<DeviceToken>(CollectionName.DeviceToken);

        await deviceTokenCollection.UpdateManyAsync(
            Builders<DeviceToken>.Filter.And(
                Builders<DeviceToken>.Filter.Eq(x => x.AccountId, account.Id),
                Builders<DeviceToken>.Filter.Eq(x => x.IsDeleted, false)),
            Builders<DeviceToken>.Update
                .Set(x => x.IsDeleted, true)
                .Set(x => x.DeletedAt, DateTime.UtcNow),
            cancellationToken : cancellationToken);

        var newRefreshTokenResult = RefreshToken.Create(account.Id);
        if(!newRefreshTokenResult.IsSuccess)
            return Result.Fail(newRefreshTokenResult.Error);

        RefreshToken newRefreshToken = newRefreshTokenResult.Value;

        await refreshTokenCollection.InsertOneAsync(
            newRefreshToken,
            cancellationToken : cancellationToken);

        string cacheKey = CacheKey.Account(account.Id);

        await _redisService.RemoveAsync(
            cacheKey,
            cancellationToken);

        return Result.Ok(new AuthResult(
            _jwtService.GenerateAccessToken(account.Id, account.Role),
            newRefreshToken.Token));
    }
}
