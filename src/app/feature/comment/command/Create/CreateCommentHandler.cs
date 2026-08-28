using AutoMapper;
using domain;
using FluentResults;
using MediatR;

namespace app;

internal sealed class CreateCommentHandler
    : IRequestHandler<CreateCommentCommand, Result<CommentDto>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IR2Service _r2Service;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public CreateCommentHandler(
        ICurrentIdentityService identityService,
        IR2Service r2Service,
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _identityService = identityService;
        _r2Service = r2Service;
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CommentDto>> Handle(
        CreateCommentCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

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
            cancellationToken : cancellationToken
        );

        var dto = _mapper.Map<CommentDto>(comment);
        var summaryDto = _mapper.Map<CommentSummaryDto>(comment);

        string cacheKey = CacheKey.Comment(comment.Id);

        await _redisService.SetAsync(
            cacheKey,
            summaryDto,
            CacheKey.DefaultCommentTTL,
            cancellationToken);

        await _dbContext.DispatchEventAsync(
            comment.Events,
            cancellationToken);

        return Result.Ok(dto);
    }
}
