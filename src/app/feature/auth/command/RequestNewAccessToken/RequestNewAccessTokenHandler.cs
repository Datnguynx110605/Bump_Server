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
            return Result.Fail("Invalid refresh token");

        var validateResult = currentToken.Validate();
        if(!validateResult.IsSuccess)
            return Result.Fail(validateResult.Error);

        var revokeResult = currentToken.Revoke();
        if(!revokeResult.IsSuccess)
            return Result.Fail(revokeResult.Error);

        var revokeFilter = Builders<RefreshToken>.Filter.And(
            Builders<RefreshToken>.Filter.Eq(x => x.Id, currentToken.Id),
            Builders<RefreshToken>.Filter.Eq(x => x.IsRevoked, false));

        var revokeUpdate = Builders<RefreshToken>.Update
            .Set(x => x.IsRevoked, currentToken.IsRevoked);

        await refreshTokenCollection.UpdateOneAsync(
            revokeFilter,
            revokeUpdate,
            cancellationToken : cancellationToken);

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var projection = Builders<Account>.Projection
            .Include(x => x.Role)
            .Include(x => x.IsDeleted);

        var account = await accountCollection
            .Find(x => x.Id == currentToken.AccountId && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail("Account not found");

        var accessToken = _jwtService.GenerateAccessToken(account.Id, account.Role);

        var newRefreshTokenResult = RefreshToken.Create(account.Id);
        if(!newRefreshTokenResult.IsSuccess)
            return Result.Fail(newRefreshTokenResult.Error);

        RefreshToken newRefreshToken = newRefreshTokenResult.Value;

        await refreshTokenCollection.InsertOneAsync(
            newRefreshToken,
            cancellationToken : cancellationToken
        );

        return Result.Ok(new AuthResult(accessToken, newRefreshToken.Token));
    }
}
