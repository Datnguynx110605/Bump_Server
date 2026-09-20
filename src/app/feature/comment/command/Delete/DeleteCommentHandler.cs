using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class DeleteCommentHandler
    : IRequestHandler<DeleteCommentCommand, Result>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly ICurrentIdentityService _identityService;
    private readonly IRabbitMQService _rabbitMQService;
    public DeleteCommentHandler(
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
        DeleteCommentCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        var projection = Builders<Comment>.Projection
            .Include(x => x.PostId)
            .Include(x => x.IsDeleted);

        var comment = await commentCollection
            .Find(x => x.Id == request.commentId && x.AccountId == identity.Value && !x.IsDeleted)
            .Project<Comment>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(comment is null)
            return Result.Fail(AppError.NotFound("Comment not found"));

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

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var postFilter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Eq(x => x.Id, comment.PostId),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false),
            // The floor is part of the precondition, not an assertion. This decrement and the
            // matching increment in CreateCommentHandler are separate writes to separate
            // collections, so an increment lost to a crash or a cancelled request leaves the
            // count one short and the deletes that follow drive it negative — and a negative
            // Amount is a value the domain refuses to construct. A zero match here means the
            // count already disagrees with reality, which is strictly better than making it
            // disagree further.
            Builders<Post>.Filter.Gt(x => x.CommentAmount.Value, 0));

        var postUpdate = Builders<Post>.Update
            .Inc(x => x.CommentAmount.Value, -1);

        // FindOneAndUpdate rather than UpdateOne so the author and shop needed for the
        // invalidations below come back in the same round trip as the decrement.
        var parentPost = await postCollection.FindOneAndUpdateAsync(
            postFilter,
            postUpdate,
            new FindOneAndUpdateOptions<Post>
            {
                ReturnDocument = ReturnDocument.After,
                Projection = Builders<Post>.Projection
                    .Include(x => x.AccountId)
                    .Include(x => x.ShopId)
            },
            cancellationToken);

        string cacheKey = CacheKey.Comment(comment.Id);

        var cacheInvalidations = new List<Task>
        {
            _redisService.RemoveAsync(
                cacheKey,
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.Post(comment.PostId),
                cancellationToken),
            // CommentAmount is part of PostProjection.Summary, so every post list built from
            // that projection carries the value this write just moved — not only post:all.
            // The rule is the projection's, not the current UI's: a thumbnail grid that does
            // not draw the count today still has the stale number in its cached payload.
            // The parent post may itself be soft-deleted, in which case the decrement above
            // matched nothing and there is no author or shop list to retire.
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
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
                $"account:{identity.Value}",
                cancellationToken)
        };

        if(parentPost is not null)
        {
            cacheInvalidations.Add(ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                $"account:{parentPost.AccountId}",
                cancellationToken));
            cacheInvalidations.Add(ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                $"shop:{parentPost.ShopId}",
                cancellationToken));
        }

        await Task.WhenAll(cacheInvalidations);

        var deleteCommentMessage = new CommentMessage(
            Guid.NewGuid(),
            identity.Value,
            comment.Id);

        await _rabbitMQService.PublishAsync(
            QueueName.DeleteComment,
            deleteCommentMessage,
            cancellationToken);

        return Result.Ok();
    }
}
