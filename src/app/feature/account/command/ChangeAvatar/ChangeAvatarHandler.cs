using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class ChangeAvatarHandler
    : IRequestHandler<ChangeAvatarCommand, Result>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly IR2Service _r2Service;
    private readonly ICurrentIdentityService _identityService;
    private readonly IRabbitMQService _rabbitMQService;
    public ChangeAvatarHandler(
        IMongoDbContext dbContext,
        IR2Service r2Service,
        IRedisService redisService,
        ICurrentIdentityService identityService,
        IRabbitMQService rabbitMQService)
    {
        _dbContext = dbContext;
        _r2Service = r2Service;
        _redisService = redisService;
        _identityService = identityService;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result> Handle(
        ChangeAvatarCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var projection = Builders<Account>.Projection
            .Include(x => x.Avatars)
            .Include(x => x.IsDeleted);

        var account = await accountCollection
            .Find(x => x.Id == identity.Value && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail(AppError.Unauthorized("Unauthenticated"));

        // Captured before the write so the superseded objects can be released after it.
        var superseded = account.Avatars.Select(x => x.Url).ToList();

        var url = await _r2Service.UploadAsync(
            request.avatars,
            request.fileName,
            cancellationToken);

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, account.Id),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false));

        // Replaces the array rather than pushing onto it. Every reader of an avatar takes
        // Avatars.LastOrDefault(), so the earlier entries were never current and never
        // readable — they only grew the document and kept their R2 objects alive, one leaked
        // image per change, forever. A single Set is also the only way to do this atomically:
        // Mongo rejects a $push and a $pull against the same path in one update.
        //
        // The field is named as a string rather than through `x => x.Avatars`. The typed
        // form compiles and then throws InvalidCastException when the update is rendered:
        // the class map binds the private `_avatars` field, whose registered serializer is
        // an IEnumerableSerializer<Avatar>, and the LINQ translator cannot cast that to a
        // serializer of the read-only collection the property exposes. Naming the element
        // skips the translation. The read side is unaffected — filters over
        // `x.Avatars.Select(...)` resolve to `Avatars.Url` correctly.
        var update = Builders<Account>.Update
            .Set("Avatars", new List<Avatar> { new Avatar(url) })
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        UpdateResult updateResult;
        try
        {
            updateResult = await accountCollection.UpdateOneAsync(
                filter,
                update,
                cancellationToken : cancellationToken);
        }
        catch(MongoWriteException)
        {
            await _r2Service.DeleteAsync(url, CancellationToken.None);
            throw;
        }

        // The account was deleted between the read and the write. Nothing references the
        // upload, so it goes back out rather than being left for a sweep that only ever
        // looks at Mongo.
        if(updateResult.MatchedCount != 1)
        {
            await _r2Service.DeleteAsync(url, CancellationToken.None);
            return Result.Fail(AppError.NotFound("Account not found"));
        }

        // Released only once the row no longer names them, so a failure above can never
        // leave the account pointing at an object that has been deleted. CancellationToken
        // .None for the same reason CreatePostHandler uses it on its cleanup path: the work
        // has committed and this has to finish regardless of the caller.
        foreach(string previous in superseded)
            await _r2Service.DeleteAsync(previous, CancellationToken.None);

        string cacheKey = CacheKey.Account(account.Id);

        await _redisService.RemoveAsync(
            cacheKey,
            cancellationToken);

        // Follow, block and react each copied the avatar onto other people's documents at
        // write time. Without this the superseded image kept rendering from those snapshots
        // — and now that the object behind it is deleted, it would render as a broken link.
        await _rabbitMQService.PublishAsync(
            QueueName.ProfileChanged,
            new ProfileMessage(Guid.NewGuid(), account.Id),
            cancellationToken);

        return Result.Ok();
    }
}
