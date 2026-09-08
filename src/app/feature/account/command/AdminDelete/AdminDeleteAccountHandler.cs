using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

// Mirrors DeleteAccountHandler for an arbitrary account. The delete-account queue
// message is republished so AccountService still runs the full cascade —
// posts, comments, reactions, follow edges and saved references.
internal sealed class AdminDeleteAccountHandler
    : IRequestHandler<AdminDeleteAccountCommand, Result>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly ICurrentIdentityService _identityService;
    private readonly IRabbitMQService _rabbitMQService;
    public AdminDeleteAccountHandler(
        IMongoDbContext dbContext,
        IRedisService redisService,
        ICurrentIdentityService identityService,
        IRabbitMQService rabbitMQService)
    {
        _dbContext = dbContext;
        _redisService = redisService;
        _identityService = identityService;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result> Handle(
        AdminDeleteAccountCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        // An administrator deleting their own account through the admin route would
        // revoke the credentials the panel is currently using; the self-service
        // endpoint on AccountController is the deliberate way to do that.
        if(identity.Value == request.accountId)
            return Result.Fail(AppError.BadRequest("Use the self-service endpoint to delete your own account"));

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var projection = Builders<Account>.Projection
            .Include(x => x.Email)
            .Include(x => x.IsDeleted);

        var account = await accountCollection
            .Find(x => x.Id == request.accountId && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail(AppError.NotFound("Account not found"));

        var result = account.DeleteAccount();
        if(!result.IsSuccess)
            return Result.Fail(result.Error);

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, request.accountId),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false));

        var update = Builders<Account>.Update
            .Set(x => x.IsDeleted, account.IsDeleted)
            .Set(x => x.DeletedAt, account.DeletedAt);

        await accountCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        await Task.WhenAll(
            _redisService.RemoveAsync(
                CacheKey.Account(request.accountId),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.AccountByEmail(account.Email),
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Account,
                filterSignature: null,
                cancellationToken));

        var message = new AccountMessage(
            Guid.NewGuid(),
            request.accountId);

        await _rabbitMQService.PublishAsync(
            QueueName.DeleteAccount,
            message,
            cancellationToken);

        return Result.Ok();
    }
}
