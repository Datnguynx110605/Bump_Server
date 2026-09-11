using app;
using domain;
using FluentResults;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace infra;

// Everything a block writes lives here rather than split with the handler: the array push
// and the severing below would otherwise sit on two independent channels, and a rapid
// block/unblock pair could apply them against each other's order.
public sealed class BlockService : IBlockService
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly ILogger<BlockService> _logger;
    public BlockService(
        IMongoDbContext dbContext,
        IRedisService redisService,
        ILogger<BlockService> logger)
    {
        _dbContext = dbContext;
        _redisService = redisService;
        _logger = logger;
    }

    public async Task<Result> ProcessBlockAsync(
        Guid blockerId,
        Guid blockedId,
        CancellationToken cancellationToken)
    {
        if(blockerId == blockedId)
            return Result.Fail("Cannot block yourself");

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var projection = Builders<Account>.Projection
            .Include(x => x.Name)
            .Include(x => x.Avatars);

        var blocked = await accountCollection
            .Find(x => x.Id == blockedId && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(blocked is null)
            return Result.Fail(AppError.NotFound("Account not found"));

        // The Blocks push runs alone so its match count is unambiguous: folded into the
        // bulk write below it would be indistinguishable from a follow pull that matched.
        var blockFilter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, blockerId),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Account>.Filter.Not(Builders<Account>.Filter.AnyEq(x => x.Blocks.Select(b => b.AccountId), blockedId)));

        var blockUpdate = Builders<Account>.Update
            .Push(x => x.Blocks, new Block(blockedId, blocked.Name, blocked.Avatars.LastOrDefault()));

        var blockResult = await accountCollection.UpdateOneAsync(
            blockFilter,
            blockUpdate,
            cancellationToken : cancellationToken);

        if(blockResult.MatchedCount != 1)
            return Result.Fail(AppError.Conflict("Already blocked this account"));

        // Severing follows is tolerant by design: strangers block each other far more often
        // than mutuals do, so a filter that matches nothing is the normal case and must not
        // fail the message. Each side carries its own filter, so a one-sided follow is
        // severed correctly without touching the other account's counter.
        var writes = new WriteModel<Account>[]
        {
            new UpdateOneModel<Account>(
                Builders<Account>.Filter.And(
                    Builders<Account>.Filter.Eq(x => x.Id, blockedId),
                    Builders<Account>.Filter.Not(Builders<Account>.Filter.AnyEq(x => x.BlockedBy, blockerId))),
                Builders<Account>.Update.Push(x => x.BlockedBy, blockerId)),
            new UpdateOneModel<Account>(
                Builders<Account>.Filter.And(
                    Builders<Account>.Filter.Eq(x => x.Id, blockerId),
                    Builders<Account>.Filter.AnyEq(x => x.Followings.Select(f => f.AccountId), blockedId)),
                Builders<Account>.Update
                    .PullFilter(x => x.Followings, f => f.AccountId == blockedId)
                    .Inc(x => x.FollowingAmount.Value, -1)),
            new UpdateOneModel<Account>(
                Builders<Account>.Filter.And(
                    Builders<Account>.Filter.Eq(x => x.Id, blockedId),
                    Builders<Account>.Filter.AnyEq(x => x.Followers.Select(f => f.AccountId), blockerId)),
                Builders<Account>.Update
                    .PullFilter(x => x.Followers, f => f.AccountId == blockerId)
                    .Inc(x => x.FollowerAmount.Value, -1)),
            new UpdateOneModel<Account>(
                Builders<Account>.Filter.And(
                    Builders<Account>.Filter.Eq(x => x.Id, blockedId),
                    Builders<Account>.Filter.AnyEq(x => x.Followings.Select(f => f.AccountId), blockerId)),
                Builders<Account>.Update
                    .PullFilter(x => x.Followings, f => f.AccountId == blockerId)
                    .Inc(x => x.FollowingAmount.Value, -1)),
            new UpdateOneModel<Account>(
                Builders<Account>.Filter.And(
                    Builders<Account>.Filter.Eq(x => x.Id, blockerId),
                    Builders<Account>.Filter.AnyEq(x => x.Followers.Select(f => f.AccountId), blockedId)),
                Builders<Account>.Update
                    .PullFilter(x => x.Followers, f => f.AccountId == blockedId)
                    .Inc(x => x.FollowerAmount.Value, -1))
        };

        await accountCollection.BulkWriteAsync(writes, cancellationToken : cancellationToken);

        var reactions = await SeverReactionsAsync(blockerId, blockedId, cancellationToken);

        await InvalidateAsync(blockerId, blockedId, reactions, cancellationToken);

        return Result.Ok();
    }

    public async Task<Result> ProcessUnblockAsync(
        Guid blockerId,
        Guid blockedId,
        CancellationToken cancellationToken)
    {
        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, blockerId),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Account>.Filter.AnyEq(x => x.Blocks.Select(b => b.AccountId), blockedId));

        var update = Builders<Account>.Update
            .PullFilter(x => x.Blocks, b => b.AccountId == blockedId);

        var result = await accountCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        if(result.MatchedCount != 1)
            return Result.Fail("Not blocked this account");

        // Unblock is deliberately not the inverse of block: it lifts the visibility barrier
        // and nothing else. Severed follows and reactions are not restored, because the
        // block did not record what it removed and re-following is the user's decision.
        await accountCollection.UpdateOneAsync(
            Builders<Account>.Filter.Eq(x => x.Id, blockedId),
            Builders<Account>.Update.Pull(x => x.BlockedBy, blockerId),
            cancellationToken : cancellationToken);

        await InvalidateAsync(blockerId, blockedId, (new List<Guid>(), new List<Guid>()), cancellationToken);

        return Result.Ok();
    }

    // Both directions, on posts and comments: a block should leave no trace of either
    // account on the other's content. Backed by the existing Reactions.AccountId multikey
    // on both collections, so these are index lookups rather than collection scans.
    private async Task<(List<Guid> PostIds, List<Guid> CommentIds)> SeverReactionsAsync(
        Guid blockerId,
        Guid blockedId,
        CancellationToken cancellationToken)
    {
        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);
        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        var postIds = new List<Guid>();
        var commentIds = new List<Guid>();

        foreach(var (ownerId, reactorId) in new[] { (blockerId, blockedId), (blockedId, blockerId) })
        {
            var postFilter = Builders<Post>.Filter.And(
                Builders<Post>.Filter.Eq(x => x.AccountId, ownerId),
                Builders<Post>.Filter.ElemMatch(x => x.Reactions, r => r.AccountId == reactorId));

            var matchedPosts = await postCollection
                .Find(postFilter)
                .Project<Post>(Builders<Post>.Projection.Include(x => x.Id))
                .ToListAsync(cancellationToken);

            if(matchedPosts.Count > 0)
            {
                await postCollection.UpdateManyAsync(
                    postFilter,
                    Builders<Post>.Update
                        .PullFilter(x => x.Reactions, r => r.AccountId == reactorId)
                        .Inc(x => x.ReactionAmount.Value, -1)
                        .Inc(x => x.CommentAmount.Value, -1),
                    cancellationToken : cancellationToken);

                postIds.AddRange(matchedPosts.Select(x => x.Id));
            }

            var commentFilter = Builders<Comment>.Filter.And(
                Builders<Comment>.Filter.Eq(x => x.AccountId, ownerId),
                Builders<Comment>.Filter.ElemMatch(x => x.Reactions, r => r.AccountId == reactorId));

            var matchedComments = await commentCollection
                .Find(commentFilter)
                .Project<Comment>(Builders<Comment>.Projection.Include(x => x.Id))
                .ToListAsync(cancellationToken);

            if(matchedComments.Count > 0)
            {
                await commentCollection.UpdateManyAsync(
                    commentFilter,
                    Builders<Comment>.Update
                        .PullFilter(x => x.Reactions, r => r.AccountId == reactorId)
                        .Inc(x => x.ReactionAmount.Value, -1),
                    cancellationToken : cancellationToken);

                commentIds.AddRange(matchedComments.Select(x => x.Id));
            }
        }

        if(postIds.Count > 0 || commentIds.Count > 0)
            _logger.LogInformation(
                "Block {BlockerId} -> {BlockedId} severed reactions on {PostCount} posts and {CommentCount} comments",
                blockerId,
                blockedId,
                postIds.Count,
                commentIds.Count);

        return (postIds, commentIds);
    }

    private async Task InvalidateAsync(
        Guid blockerId,
        Guid blockedId,
        (List<Guid> PostIds, List<Guid> CommentIds) reactions,
        CancellationToken cancellationToken)
    {
        var tasks = new List<Task>
        {
            _redisService.RemoveAsync(CacheKey.Account(blockerId), cancellationToken),
            _redisService.RemoveAsync(CacheKey.Account(blockedId), cancellationToken),
            _redisService.RemoveAsync(CacheKey.BlockedSet(blockerId), cancellationToken),
            _redisService.RemoveAsync(CacheKey.BlockedSet(blockedId), cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                $"{CollectionName.Account}:block",
                $"account:{blockerId}",
                cancellationToken)
        };

        foreach(var accountId in new[] { blockerId, blockedId })
        {
            tasks.Add(ListCache.InvalidateAsync(
                _redisService,
                $"{CollectionName.Account}:following",
                $"account:{accountId}",
                cancellationToken));
            tasks.Add(ListCache.InvalidateAsync(
                _redisService,
                $"{CollectionName.Account}:follower",
                $"account:{accountId}",
                cancellationToken));
        }

        foreach(var postId in reactions.PostIds)
        {
            tasks.Add(_redisService.RemoveAsync(CacheKey.Post(postId), cancellationToken));
            tasks.Add(ListCache.InvalidateAsync(
                _redisService,
                $"{CollectionName.Post}:reaction",
                $"post:{postId}",
                cancellationToken));
        }

        foreach(var commentId in reactions.CommentIds)
        {
            tasks.Add(_redisService.RemoveAsync(CacheKey.Comment(commentId), cancellationToken));
            tasks.Add(ListCache.InvalidateAsync(
                _redisService,
                $"{CollectionName.Comment}:reaction",
                $"comment:{commentId}",
                cancellationToken));
        }

        await Task.WhenAll(tasks);
    }
}
