using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

// Revocation is the only write the refresh-token store needs from an administrator:
// rows expire on their own through the TTL index on ExpiryDate, so deleting them is
// never necessary, while revoking cuts an active session off immediately.
internal sealed class RevokeRefreshTokenHandler
    : IRequestHandler<RevokeRefreshTokenCommand, Result>
{
    private readonly IMongoDbContext _dbContext;
    public RevokeRefreshTokenHandler(IMongoDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        RevokeRefreshTokenCommand request,
        CancellationToken cancellationToken)
    {
        var refreshTokenCollection = _dbContext.GetCollection<RefreshToken>(CollectionName.RefreshToken);

        var refreshToken = await refreshTokenCollection
            .Find(x => x.Id == request.refreshTokenId && !x.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if(refreshToken is null)
            return Result.Fail(AppError.NotFound("Refresh token not found"));

        var result = refreshToken.Revoke();
        if(!result.IsSuccess)
            return Result.Fail(AppError.Conflict(result.Error!));

        var filter = Builders<RefreshToken>.Filter.And(
            Builders<RefreshToken>.Filter.Eq(x => x.Id, refreshToken.Id),
            Builders<RefreshToken>.Filter.Eq(x => x.IsRevoked, false));

        var update = Builders<RefreshToken>.Update
            .Set(x => x.IsRevoked, refreshToken.IsRevoked);

        await refreshTokenCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        return Result.Ok();
    }
}
