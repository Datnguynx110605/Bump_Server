using app;
using domain;
using FluentResults;
using MongoDB.Driver;

namespace infra;

public sealed class FollowService : IFollowService
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly INotificationService _notificationSerivce;
    public FollowService(
        IMongoDbContext dbContext,
        IRedisService redisService,
        INotificationService notificationService)
    {
        _dbContext = dbContext;
        _redisService = redisService;
        _notificationSerivce = notificationService;
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
            return Result.Fail("Follower account not found");

        var following = await accountCollection
            .Find(x => x.Id == followingId && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(following is null)
            return Result.Fail("Following account not found");

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

        if(bulkResult.MatchedCount == 1)
            throw new InvalidOperationException("Follow partially applied: only one side of the relationship was written");

        if(bulkResult.MatchedCount != 2)
            return Result.Fail("Already following");

        await Task.WhenAll(
            _redisService.RemoveAsync(
                CacheKey.Account(followerId),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.Account(followingId),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List($"{CollectionName.Account}:following", $"account:{followerId}", new CursorPageRequest(null)),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List($"{CollectionName.Account}:follower", $"account:{followingId}", new CursorPageRequest(null)),
                cancellationToken));

        return await _notificationSerivce.CreateAsync(
            following.Id,
            "New follower",
            $"{follower.Name.Value} followed you",
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

        if(bulkResult.MatchedCount == 1)
            throw new InvalidOperationException("Unfollow partially applied: only one side of the relationship was removed");

        if(bulkResult.MatchedCount != 2)
            return Result.Fail("Not following this account");

        await Task.WhenAll(
            _redisService.RemoveAsync(
                CacheKey.Account(followerId),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.Account(followingId),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List($"{CollectionName.Account}:following", $"account:{followerId}", new CursorPageRequest(null)),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List($"{CollectionName.Account}:follower", $"account:{followingId}", new CursorPageRequest(null)),
                cancellationToken));

        return Result.Ok();
    }
}
