using app;
using domain;
using FluentResults;
using MongoDB.Driver;

namespace infra;

public sealed class DeleteAccountService : IDeleteAccountService
{
    private readonly IMongoDbContext _dbContext;
    private readonly IR2Service _r2Service;
    private readonly IRedisService _redisService;
    public DeleteAccountService(
        IMongoDbContext dbContext,
        IR2Service r2Service,
        IRedisService redisService)
    {
        _dbContext = dbContext;
        _r2Service = r2Service;
        _redisService = redisService;
    }

    public async Task<Result> ProcessAfterDeleteAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var accountProjection = Builders<Account>.Projection
            .Include(x => x.Avatars);

        var account = await accountCollection
            .Find(x => x.Id == accountId && x.IsDeleted)
            .Project<Account>(accountProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail("Account not found");

        foreach(var avatar in account.Avatars)
        {
            await _r2Service.DeleteAsync(
                avatar.Url,
                cancellationToken);
        }

        var refreshTokenFilter = Builders<RefreshToken>.Filter.And(
            Builders<RefreshToken>.Filter.Eq(x => x.AccountId, accountId),
            Builders<RefreshToken>.Filter.Eq(x => x.IsRevoked, false));

        var refreshTokenUpdate = Builders<RefreshToken>.Update
            .Set(x => x.IsRevoked, true);

        var ownerCommentFilter = Builders<Comment>.Filter.And(
            Builders<Comment>.Filter.Eq(x => x.AccountId, account.Id),
            Builders<Comment>.Filter.Eq(x => x.IsDeleted, false));

        var ownerCommentUpdate = Builders<Comment>.Update
            .Set(x => x.IsDeleted, true);

        var postFilter = Builders<Post>.Filter.ElemMatch(x => x.Reactions, r => r.AccountId == account.Id);

        var postUpdate = Builders<Post>.Update
            .PullFilter(x => x.Reactions, f => f.AccountId == account.Id)
            .Inc(x => x.ReactionAmount.Value, -1);

        var commentFilter = Builders<Comment>.Filter.ElemMatch(x => x.Reactions, r => r.AccountId == account.Id);

        var commentUpdate = Builders<Comment>.Update
            .PullFilter(x => x.Reactions, f => f.AccountId == account.Id)
            .Inc(x => x.ReactionAmount.Value, -1);

        var followerFilter = Builders<Account>.Filter.ElemMatch(x => x.Followers, r => r.AccountId == account.Id);

        var followerUpdate = Builders<Account>.Update
            .PullFilter(x => x.Followers, f => f.AccountId == account.Id)
            .Inc(x => x.FollowerAmount.Value, -1);

        var followingFilter = Builders<Account>.Filter.ElemMatch(x => x.Followings, r => r.AccountId == account.Id);

        var followingUpdate = Builders<Account>.Update
            .PullFilter(x => x.Followings, f => f.AccountId == account.Id)
            .Inc(x => x.FollowingAmount.Value, -1);

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var refreshTokenCollection = _dbContext.GetCollection<RefreshToken>(CollectionName.RefreshToken);

        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        var ownerComments = await commentCollection
            .Find(ownerCommentFilter)
            .Project<Comment>(Builders<Comment>.Projection.Include(x => x.Id).Include(x => x.PostId))
            .ToListAsync(cancellationToken);

        var reactedPosts = await postCollection
            .Find(postFilter)
            .Project<Post>(Builders<Post>.Projection.Include(x => x.Id))
            .ToListAsync(cancellationToken);

        var reactedComments = await commentCollection
            .Find(commentFilter)
            .Project<Comment>(Builders<Comment>.Projection.Include(x => x.Id))
            .ToListAsync(cancellationToken);

        var followerAccounts = await accountCollection
            .Find(followerFilter)
            .Project<Account>(Builders<Account>.Projection.Include(x => x.Id))
            .ToListAsync(cancellationToken);

        var followingAccounts = await accountCollection
            .Find(followingFilter)
            .Project<Account>(Builders<Account>.Projection.Include(x => x.Id))
            .ToListAsync(cancellationToken);

        await Task.WhenAll(
            refreshTokenCollection.UpdateManyAsync(
                refreshTokenFilter,
                refreshTokenUpdate,
                cancellationToken : cancellationToken),
            postCollection.UpdateManyAsync(
                postFilter,
                postUpdate,
                cancellationToken : cancellationToken),
            commentCollection.UpdateManyAsync(
                commentFilter,
                commentUpdate,
                cancellationToken : cancellationToken),
            commentCollection.UpdateManyAsync(
                ownerCommentFilter,
                ownerCommentUpdate,
                cancellationToken : cancellationToken),
            accountCollection.UpdateManyAsync(
                followerFilter,
                followerUpdate,
                cancellationToken : cancellationToken),
            accountCollection.UpdateManyAsync(
                followingFilter,
                followingUpdate,
                cancellationToken : cancellationToken));

        var commentCountByPost = ownerComments
            .GroupBy(x => x.PostId)
            .ToList();

        if(commentCountByPost.Count > 0)
        {
            var commentAmountWrites = commentCountByPost
                .Select(group => new UpdateOneModel<Post>(
                    Builders<Post>.Filter.Eq(x => x.Id, group.Key),
                    Builders<Post>.Update.Inc(x => x.CommentAmount.Value, -group.Count())))
                .ToList<WriteModel<Post>>();

            await postCollection.BulkWriteAsync(commentAmountWrites, cancellationToken : cancellationToken);
        }

        var cacheInvalidations = new List<Task>
        {
            _redisService.RemoveAsync(CacheKey.Account(account.Id), cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List(CollectionName.Comment, $"account:{account.Id}", new CursorPageRequest(null)),
                cancellationToken)
        };

        foreach(var post in reactedPosts)
        {
            cacheInvalidations.Add(_redisService.RemoveAsync(CacheKey.Post(post.Id), cancellationToken));
            cacheInvalidations.Add(_redisService.RemoveAsync(
                CacheKey.List($"{CollectionName.Post}:reaction", $"post:{post.Id}", new CursorPageRequest(null)),
                cancellationToken));
        }

        foreach(var comment in reactedComments)
        {
            cacheInvalidations.Add(_redisService.RemoveAsync(CacheKey.Comment(comment.Id), cancellationToken));
            cacheInvalidations.Add(_redisService.RemoveAsync(
                CacheKey.List($"{CollectionName.Comment}:reaction", $"comment:{comment.Id}", new CursorPageRequest(null)),
                cancellationToken));
        }

        foreach(var comment in ownerComments)
        {
            cacheInvalidations.Add(_redisService.RemoveAsync(CacheKey.Comment(comment.Id), cancellationToken));
        }

        foreach(var group in commentCountByPost)
        {
            cacheInvalidations.Add(_redisService.RemoveAsync(CacheKey.Post(group.Key), cancellationToken));
            cacheInvalidations.Add(_redisService.RemoveAsync(
                CacheKey.List(CollectionName.Comment, $"post:{group.Key}", new CursorPageRequest(null)),
                cancellationToken));
        }

        foreach(var follower in followerAccounts)
        {
            cacheInvalidations.Add(_redisService.RemoveAsync(CacheKey.Account(follower.Id), cancellationToken));
        }

        foreach(var following in followingAccounts)
        {
            cacheInvalidations.Add(_redisService.RemoveAsync(CacheKey.Account(following.Id), cancellationToken));
        }

        await Task.WhenAll(cacheInvalidations);

        return Result.Ok();
    }
}
