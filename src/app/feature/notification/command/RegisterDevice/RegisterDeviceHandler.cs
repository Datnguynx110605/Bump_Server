using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class RegisterDeviceHandler
    : IRequestHandler<RegisterDeviceCommand, Result>
{
    private readonly IMongoDbContext _dbContext;
    private readonly ICurrentIdentityService _identityService;
    public RegisterDeviceHandler(
        IMongoDbContext dbContext,
        ICurrentIdentityService identityService)
    {
        _dbContext = dbContext;
        _identityService = identityService;
    }

    public async Task<Result> Handle(
        RegisterDeviceCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        ApnsEnvironment environment = request.isSandbox
            ? ApnsEnvironment.Sandbox
            : ApnsEnvironment.Production;

        // The factory still validates and mints the id and timestamp; the write itself is a
        // conditional update, as everywhere else in this codebase.
        var deviceTokenResult = DeviceToken.Create(
            identity.Value,
            request.token,
            environment);

        if(!deviceTokenResult.IsSuccess)
            return Result.Fail(deviceTokenResult.Error);

        DeviceToken deviceToken = deviceTokenResult.Value!;

        var deviceTokenCollection = _dbContext.GetCollection<DeviceToken>(CollectionName.DeviceToken);

        var filter = Builders<DeviceToken>.Filter.Eq(x => x.Token, request.token);

        // An upsert, not an insert. iOS hands the same device token to whoever signs in on
        // that phone, so a plain insert would throw on the unique index and leave the new
        // owner's pushes going to the previous one. Clearing IsDeleted also revives a row
        // the APNs pruning retired earlier, which is what a reinstall on the same device
        // looks like.
        var update = Builders<DeviceToken>.Update
            .Set(x => x.AccountId, identity.Value)
            .Set(x => x.Environment, environment)
            .Set(x => x.UpdatedAt, DateTime.UtcNow)
            .Set(x => x.IsDeleted, false)
            .Set(x => x.DeletedAt, null)
            .SetOnInsert(x => x.Id, deviceToken.Id)
            .SetOnInsert(x => x.CreatedAt, deviceToken.CreatedAt);

        await deviceTokenCollection.UpdateOneAsync(
            filter,
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken);

        return Result.Ok();
    }
}
