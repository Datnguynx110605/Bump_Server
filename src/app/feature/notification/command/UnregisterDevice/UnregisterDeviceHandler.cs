using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class UnregisterDeviceHandler
    : IRequestHandler<UnregisterDeviceCommand, Result>
{
    private readonly IMongoDbContext _dbContext;
    private readonly ICurrentIdentityService _identityService;
    public UnregisterDeviceHandler(
        IMongoDbContext dbContext,
        ICurrentIdentityService identityService)
    {
        _dbContext = dbContext;
        _identityService = identityService;
    }

    public async Task<Result> Handle(
        UnregisterDeviceCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        var deviceTokenCollection = _dbContext.GetCollection<DeviceToken>(CollectionName.DeviceToken);

        // Matched on the account as well as the token: on the token alone, any signed-in
        // account could retire another account's device.
        var filter = Builders<DeviceToken>.Filter.And(
            Builders<DeviceToken>.Filter.Eq(x => x.Token, request.token),
            Builders<DeviceToken>.Filter.Eq(x => x.AccountId, identity.Value),
            Builders<DeviceToken>.Filter.Eq(x => x.IsDeleted, false));

        var update = Builders<DeviceToken>.Update
            .Set(x => x.IsDeleted, true)
            .Set(x => x.DeletedAt, DateTime.UtcNow);

        // A miss is success: the caller wanted this device to stop receiving pushes, and it
        // already does.
        await deviceTokenCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        return Result.Ok();
    }
}
