using app;
using domain;
using FluentResults;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace infra;

public sealed class FollowService : IFollowService
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly INotificationService _notificationSerivce;
    private readonly ILogger<FollowService> _logger;
    public FollowService(
        IMongoDbContext dbContext,
        IRedisService redisService,
        INotificationService notificationService,
        ILogger<FollowService> logger)
    {
        _dbContext = dbContext;
        _redisService = redisService;
        _notificationSerivce = notificationService;
        _logger = logger;
    }

    public async Task<Result> ProcessFollowAsync(
        Guid followerId,
        Guid followingId,
        CancellationToken cancellationToken)
    {
        if(followerId == followingId)
            return Result.Fail("Cannot follow yourself");

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var projection = Builders<Account>.Projection
            .Include(x => x.Name)
            .Include(x => x.Avatars);

        var follower = await accountCollection
            .Find(x => x.Id == followerId && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(follower is null)
            return Result.Fail(AppError.NotFound("Follower account not found"));

        var following = await accountCollection
            .Find(x => x.Id == followingId && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(following is null)
            return Result.Fail(AppError.NotFound("Following account not found"));

        // Silently refused, not reported: the endpoint already answered 202, and an error
        // here would tell the actor that the other account blocked them. A failed Result is
        // logged by QueueConsumerHost and the message is not retried.
        if(await BlockGuard.IsBlockedAsync(accountCollection, followerId, followingId, cancellationToken))
            return Result.Fail("Blocked by or blocking this account");

        var followerFilter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, followerId),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Account>.Filter.Not(Builders<Account>.Filter.AnyEq(x => x.Followings.Select(f => f.AccountId), followingId)));

        var followingFilter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, followingId),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Account>.Filter.Not(Builders<Account>.Filter.AnyEq(x => x.Followers.Select(f => f.AccountId), followerId)));

        var followerUpdate = Builders<Account>.Update
            .Push(x => x.Followings, new Following(followingId, following.Name, following.Avatars.LastOrDefault()))
            .Inc(x => x.FollowingAmount.Value, 1);

        var followingUpdate = Builders<Account>.Update
            .Push(x => x.Followers, new Follower(followerId, follower.Name, follower.Avatars.LastOrDefault()))
            .Inc(x => x.FollowerAmount.Value, 1);

        var bulkResult = await accountCollection.BulkWriteAsync(
            new WriteModel<Account>[]
            {
                new UpdateOneModel<Account>(followerFilter, followerUpdate),
                new UpdateOneModel<Account>(followingFilter, followingUpdate)
            },
            cancellationToken : cancellationToken);

        // Each side's filter excludes the state it writes, so a match count of 1 means the
        // other side was already correct and this write repaired a previously half-applied
        // follow. Throwing here used to guarantee the drift instead of healing it: every
        // retry rethrew, the message hit the DLQ, and the counters stayed inconsistent.
        if(bulkResult.MatchedCount == 0)
            return Result.Fail(AppError.Conflict("Already following"));

        if(bulkResult.MatchedCount == 1)
            _logger.LogWarning(
                "Follow {FollowerId} -> {FollowingId} was half-applied and has been repaired",
                followerId,
                followingId);

        await Task.WhenAll(
            _redisService.RemoveAsync(
                CacheKey.Account(followerId),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.Account(followingId),
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                $"{CollectionName.Account}:following",
                $"account:{followerId}",
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                $"{CollectionName.Account}:follower",
                $"account:{followingId}",
                cancellationToken));

        return await _notificationSerivce.CreateAsync(
            following.Id,
            "New follower",
            $"{follower.Name.Value} followed you",
            NotificationType.Follow,
            follower.Id,
            follower.Id,
            follower.Avatars.LastOrDefault(),
            cancellationToken);
    }

    public async Task<Result> ProcessUnfollowAsync(
        Guid followerId,
        Guid followingId,
        CancellationToken cancellationToken)
    {
        if(followerId == followingId)
            return Result.Fail("Cannot unfollow yourself");

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var followerFilter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, followerId),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Account>.Filter.AnyEq(x => x.Followings.Select(f => f.AccountId), followingId));

        var followingFilter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, followingId),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Account>.Filter.AnyEq(x => x.Followers.Select(f => f.AccountId), followerId));

        var followerUpdate = Builders<Account>.Update
            .PullFilter(x => x.Followings, f => f.AccountId == followingId)
            .Inc(x => x.FollowingAmount.Value, -1);

        var followingUpdate = Builders<Account>.Update
            .PullFilter(x => x.Followers, f => f.AccountId == followerId)
            .Inc(x => x.FollowerAmount.Value, -1);

        var bulkResult = await accountCollection.BulkWriteAsync(
            new WriteModel<Account>[]
            {
                new UpdateOneModel<Account>(followerFilter, followerUpdate),
                new UpdateOneModel<Account>(followingFilter, followingUpdate)
            },
            cancellationToken : cancellationToken);

        // Same reasoning as ProcessFollowAsync: 1 means the other side was already removed
        // and this write repaired the drift.
        if(bulkResult.MatchedCount == 0)
            return Result.Fail("Not following this account");

        if(bulkResult.MatchedCount == 1)
            _logger.LogWarning(
                "Unfollow {FollowerId} -> {FollowingId} was half-applied and has been repaired",
                followerId,
                followingId);

        await Task.WhenAll(
            _redisService.RemoveAsync(
                CacheKey.Account(followerId),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.Account(followingId),
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                $"{CollectionName.Account}:following",
                $"account:{followerId}",
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                $"{CollectionName.Account}:follower",
                $"account:{followingId}",
                cancellationToken));

        return Result.Ok();
    }
}
