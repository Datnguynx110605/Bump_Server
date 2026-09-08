using app;
using domain;
using FluentResults;
using MongoDB.Driver;

namespace infra;

public sealed class DeletePostService : IDeletePostService
{
    private readonly IMongoDbContext _dbContext;
    private readonly IR2Service _r2Service;
    private readonly IRedisService _redisService;
    public DeletePostService(
        IMongoDbContext dbContext,
        IR2Service r2Service,
        IRedisService redisService)
    {
        _dbContext = dbContext;
        _r2Service = r2Service;
        _redisService = redisService;
    }

    public async Task<Result> ProcessAfterDeleteAsync(
        Guid postId,
        CancellationToken cancellationToken)
    {
        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var postProjection = Builders<Post>.Projection
            .Include(x => x.Id);

        var post = await postCollection
            .Find(x => x.Id == postId && x.IsDeleted)
            .Project<Post>(postProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if(post is null)
            return Result.Fail(AppError.NotFound("Post not found"));

        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        var commentProjection = Builders<Comment>.Projection
            .Include(x => x.AccountId)
            .Include(x => x.Images);

        var comments = await commentCollection
            .Find(x => x.PostId == post.Id && !x.IsDeleted)
            .Project<Comment>(commentProjection)
            .ToListAsync(cancellationToken);

        if(comments.Count == 0)
            return Result.Ok();

        foreach(var comment in comments)
        {
            foreach(var image in comment.Images)
            {
                await _r2Service.DeleteAsync(
                    image.Url,
                    cancellationToken);
            }
        }

        var commentFilter = Builders<Comment>.Filter.And(
            Builders<Comment>.Filter.Eq(x => x.PostId, post.Id),
            Builders<Comment>.Filter.Eq(x => x.IsDeleted, false));

        var commentUpdate = Builders<Comment>.Update
            .Set(x => x.IsDeleted, true)
            .Set(x => x.DeletedAt, DateTime.UtcNow);

        await commentCollection.UpdateManyAsync(
            commentFilter,
            commentUpdate,
            cancellationToken : cancellationToken);

        var cacheInvalidations = new List<Task>
        {
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Comment,
                $"post:{post.Id}",
                cancellationToken)
        };

        foreach(var comment in comments)
        {
            cacheInvalidations.Add(_redisService.RemoveAsync(CacheKey.Comment(comment.Id), cancellationToken));
        }

        foreach(var authorId in comments.Select(x => x.AccountId).Distinct())
        {
            cacheInvalidations.Add(ListCache.InvalidateAsync(
    _redisService,
    CollectionName.Comment,
    $"account:{authorId}",
    cancellationToken));
        }

        await Task.WhenAll(cacheInvalidations);

        return Result.Ok();
    }
}
