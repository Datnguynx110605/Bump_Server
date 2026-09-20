using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class CreateCommentHandler
    : IRequestHandler<CreateCommentCommand, Result<CommentDto>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IR2Service _r2Service;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    private readonly IRabbitMQService _rabbitMQService;
    public CreateCommentHandler(
        ICurrentIdentityService identityService,
        IR2Service r2Service,
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper,
        IRabbitMQService rabbitMQService)
    {
        _identityService = identityService;
        _r2Service = r2Service;
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result<CommentDto>> Handle(
        CreateCommentCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        // Resolved up front, before any image is uploaded: rejecting after the upload would
        // leave orphans in R2 that nothing references. Unlike follow and reaction this
        // handler answers synchronously, so it cannot silently drop the write.
        var targetPost = await postCollection
            .Find(x => x.Id == request.postId && !x.IsDeleted)
            .Project<Post>(Builders<Post>.Projection
                .Include(x => x.AccountId)
                .Include(x => x.ShopId))
            .FirstOrDefaultAsync(cancellationToken);

        // 404 for both "no such post" and "blocked", so a blocked post is indistinguishable
        // from a deleted one and the block is not revealed — the same answer
        // GetSpecificPostHandler gives a blocked reader.
        if(targetPost is null)
            return Result.Fail(AppError.NotFound("Post not found"));

        if(await BlockGuard.IsBlockedAsync(
            _dbContext.GetCollection<Account>(CollectionName.Account),
            identity.Value,
            targetPost.AccountId,
            cancellationToken))
            return Result.Fail(AppError.NotFound("Post not found"));

        List<string>? urls = null;
        if(request.images?.Any() == true)
        {
            // Uploaded together rather than one after another. Up to ten files at a couple
            // of hundred milliseconds each is seconds of wall clock inside a request the
            // P95 budget gives 500ms, and R2 has no reason to be fed serially. Ordering is
            // preserved because Task.WhenAll returns results in the order the tasks were
            // created, which matters: the images array is what the client renders.
            urls = (await Task.WhenAll(request.images.Select(image =>
                _r2Service.UploadAsync(
                    image.stream,
                    image.fileName,
                    cancellationToken)))).ToList();
        }

        // See CreatePostHandler: the upload is already in R2 and nothing references it until
        // the comment row exists, so a domain failure below has to remove it too.
        async Task DiscardUploadsAsync()
        {
            if(urls is null)
                return;

            foreach(var uploaded in urls)
                await _r2Service.DeleteAsync(uploaded, CancellationToken.None);
        }

        var commentResult = Comment.Create(
            identity.Value,
            request.postId,
            request.content,
            urls);
        if(!commentResult.IsSuccess)
        {
            await DiscardUploadsAsync();
            return Result.Fail(commentResult.Error);
        }

        Comment comment = commentResult.Value;

        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        try
        {
            await commentCollection.InsertOneAsync(
                comment,
                cancellationToken : cancellationToken);
        }
        catch(MongoWriteException)
        {
            await DiscardUploadsAsync();
            throw;
        }

        var filter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Eq(x => x.Id, comment.PostId),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false));

        var update = Builders<Post>.Update
            .Inc(x => x.CommentAmount.Value, 1);

        await postCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);
        
        var dto = _mapper.Map<CommentDto>(comment);
        var summaryDto = _mapper.Map<CommentSummaryDto>(comment);

        string cacheKey = CacheKey.Comment(comment.Id);

        await Task.WhenAll(
            _redisService.SetAsync(
                cacheKey,
                summaryDto,
                CacheKey.DefaultCommentTTL,
                cancellationToken),
            _redisService.RemoveAsync(
                CacheKey.Post(comment.PostId),
                cancellationToken),
            // CommentAmount is part of PostProjection.Summary, so every post list built from
            // that projection carries the value this write just moved — not only post:all.
            // The rule is the projection's, not the current UI's: a thumbnail grid that does
            // not draw the count today still has the stale number in its cached payload.
            // ChangeCaptionHandler and ReactPostService bump the same three for the same
            // reason.
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                filterSignature: null,
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                $"account:{targetPost.AccountId}",
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                $"shop:{targetPost.ShopId}",
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
                cancellationToken));

        var message = new CommentMessage(
            Guid.NewGuid(),
            identity.Value,
            comment.Id);

        await _rabbitMQService.PublishAsync(
            QueueName.CreateComment,
            message,
            cancellationToken);

        return Result.Ok(dto);
    }
}
