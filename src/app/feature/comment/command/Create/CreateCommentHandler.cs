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
            .Project<Post>(Builders<Post>.Projection.Include(x => x.AccountId))
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
            urls = new List<string>();
            foreach(var image in request.images)
            {
                var url = await _r2Service.UploadAsync(
                    image.stream,
                    image.fileName,
                    cancellationToken);

                urls.Add(url);
            }
        }

        var commentResult = Comment.Create(
            identity.Value,
            request.postId,
            request.content,
            urls);
        if(!commentResult.IsSuccess)
            return Result.Fail(commentResult.Error);

        Comment comment = commentResult.Value;

        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        await commentCollection.InsertOneAsync(
            comment,
            cancellationToken : cancellationToken);

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
            // The admin post listing renders CommentAmount off the Post document, so moving it
            // has to retire post:all. The account: and shop: signatures are deliberately left
            // alone — those lists are thumbnail grids with no counts on them.
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
