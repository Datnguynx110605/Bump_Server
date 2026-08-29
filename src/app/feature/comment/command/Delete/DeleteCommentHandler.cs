using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class DeleteCommentHandler
    : IRequestHandler<DeleteCommentCommand, Result>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IR2Service _r2Service;
    private readonly IRedisService _redisService;
    private readonly ICurrentIdentityService _identityService;
    public DeleteCommentHandler(
        IMongoDbContext dbContext,
        IR2Service r2Service,
        IRedisService redisService,
        ICurrentIdentityService identityService)
    {
        _dbContext = dbContext;
        _r2Service = r2Service;
        _redisService = redisService;
        _identityService = identityService;
    }

    public async Task<Result> Handle(
        DeleteCommentCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        var projection = Builders<Comment>.Projection
            .Include(x => x.PostId)
            .Include(x => x.Images)
            .Include(x => x.IsDeleted);

        var comment = await commentCollection
            .Find(x => x.Id == request.commentId && x.AccountId == identity.Value && !x.IsDeleted)
            .Project<Comment>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(comment is null)
            return Result.Fail("Comment not found");

        var result = comment.DeleteComment();
        if(!result.IsSuccess)
            return Result.Fail(result.Error);

        var filter = Builders<Comment>.Filter.And(
            Builders<Comment>.Filter.Eq(x => x.Id, comment.Id),
            Builders<Comment>.Filter.Eq(x => x.AccountId, identity.Value),
            Builders<Comment>.Filter.Eq(x => x.IsDeleted, false));

        var update = Builders<Comment>.Update
            .Set(x => x.IsDeleted, comment.IsDeleted)
            .Set(x => x.DeletedAt, comment.DeletedAt);

        await commentCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        foreach(var image in comment.Images)
        {
            await _r2Service.DeleteAsync(
                image.Url,
                cancellationToken);
        }

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var postFilter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Eq(x => x.Id, comment.PostId),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false));

        var postUpdate = Builders<Post>.Update
            .Inc(x => x.CommentAmount.Value, -1);

        await postCollection.UpdateOneAsync(
            postFilter,
            postUpdate,
            cancellationToken : cancellationToken);

        string cacheKey = CacheKey.Comment(comment.Id);

        await Task.WhenAll(
            _redisService.RemoveAsync(
                cacheKey,
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.Post(comment.PostId),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List(CollectionName.Comment, $"post:{comment.PostId}", new CursorPageRequest(null)),
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.List(CollectionName.Comment, $"account:{identity.Value}", new CursorPageRequest(null)),
                cancellationToken));

        return Result.Ok();
    }
}
