using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class RequestNewAccessTokenHandler
    : IRequestHandler<RequestNewAccessTokenCommand, Result<AuthResult>>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IJwtService _jwtService;
    public RequestNewAccessTokenHandler(
        IMongoDbContext dbContext,
        IJwtService jwtService)
    {
        _dbContext = dbContext;
        _jwtService = jwtService;
    }

    public async Task<Result<AuthResult>> Handle(
        RequestNewAccessTokenCommand request,
        CancellationToken cancellationToken)
    {
        var refreshTokenCollection = _dbContext.GetCollection<RefreshToken>(CollectionName.RefreshToken);

        var currentToken = await refreshTokenCollection
            .Find(x => x.Token == request.refreshToken)
            .FirstOrDefaultAsync(cancellationToken);

        if(currentToken is null)
            return Result.Fail(AppError.Unauthorized("Invalid refresh token"));

        var validateResult = currentToken.Validate();
        if(!validateResult.IsSuccess)
            return Result.Fail(AppError.Unauthorized(validateResult.Error!));

        var revokeResult = currentToken.Revoke();
        if(!revokeResult.IsSuccess)
            return Result.Fail(AppError.Unauthorized(revokeResult.Error!));

        var revokeFilter = Builders<RefreshToken>.Filter.And(
            Builders<RefreshToken>.Filter.Eq(x => x.Id, currentToken.Id),
            Builders<RefreshToken>.Filter.Eq(x => x.IsRevoked, false));

        var revokeUpdate = Builders<RefreshToken>.Update
            .Set(x => x.IsRevoked, currentToken.IsRevoked);

        var revokeWrite = await refreshTokenCollection.UpdateOneAsync(
            revokeFilter,
            revokeUpdate,
            cancellationToken : cancellationToken);

        // The filter is the concurrency control, not a formality: two requests presenting
        // the same token both read it unrevoked and both reach this write, but only one
        // matches. Without this check both were issued a fresh pair, so a stolen token
        // could be replayed alongside the legitimate client and neither was cut off.
        if(revokeWrite.MatchedCount != 1)
            return Result.Fail(AppError.Unauthorized("Invalid refresh token"));

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var projection = Builders<Account>.Projection
            .Include(x => x.Role)
            .Include(x => x.IsDeleted);

        var account = await accountCollection
            .Find(x => x.Id == currentToken.AccountId && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail(AppError.NotFound("Account not found"));

        var accessToken = _jwtService.GenerateAccessToken(account.Id, account.Role);

        var newRefreshTokenResult = RefreshToken.Create(account.Id);
        if(!newRefreshTokenResult.IsSuccess)
            return Result.Fail(newRefreshTokenResult.Error);

        RefreshToken newRefreshToken = newRefreshTokenResult.Value;

        await refreshTokenCollection.InsertOneAsync(
            newRefreshToken,
            cancellationToken : cancellationToken);

        return Result.Ok(new AuthResult(accessToken, newRefreshToken.Token));
    }
}
