using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

// Mirrors DeleteCommentHandler without the ownership filter. The parent post's
// CommentAmount decrement, both list-cache invalidations and the delete-comment message
// that removes the images are replicated, since dropping them would leave the post's
// comment count permanently overstated and the images orphaned in R2.
internal sealed class AdminDeleteCommentHandler
    : IRequestHandler<AdminDeleteCommentCommand, Result>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly IRabbitMQService _rabbitMQService;
    public AdminDeleteCommentHandler(
        IMongoDbContext dbContext,
        IRedisService redisService,
        IRabbitMQService rabbitMQService)
    {
        _dbContext = dbContext;
        _redisService = redisService;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result> Handle(
        AdminDeleteCommentCommand request,
        CancellationToken cancellationToken)
    {
        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        var projection = Builders<Comment>.Projection
            .Include(x => x.AccountId)
            .Include(x => x.PostId)
            .Include(x => x.IsDeleted);

        var comment = await commentCollection
            .Find(x => x.Id == request.commentId && !x.IsDeleted)
            .Project<Comment>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(comment is null)
            return Result.Fail(AppError.NotFound("Comment not found"));

        var result = comment.DeleteComment();
        if(!result.IsSuccess)
            return Result.Fail(result.Error);

        var filter = Builders<Comment>.Filter.And(
            Builders<Comment>.Filter.Eq(x => x.Id, comment.Id),
            Builders<Comment>.Filter.Eq(x => x.IsDeleted, false));

        var update = Builders<Comment>.Update
            .Set(x => x.IsDeleted, comment.IsDeleted)
            .Set(x => x.DeletedAt, comment.DeletedAt);

        await commentCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

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
            // The feed cards render CommentAmount off the Post document, so moving it has to
            // retire the two post lists that show it. The account: and shop: signatures are
            // deliberately left alone — those lists are thumbnail grids with no counts on them.
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                filterSignature: null,
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                "mostReacted",
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Comment,
                filterSignature: null,
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Comment,
                $"post:{comment.PostId}",
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Comment,
                $"account:{comment.AccountId}",
                cancellationToken));

        var deleteCommentMessage = new CommentMessage(
            Guid.NewGuid(),
            comment.AccountId,
            comment.Id);

        await _rabbitMQService.PublishAsync(
            QueueName.DeleteComment,
            deleteCommentMessage,
            cancellationToken);

        return Result.Ok();
    }
}
