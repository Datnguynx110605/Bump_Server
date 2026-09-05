using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetSpecificCommentHandler
    : IRequestHandler<GetSpecificCommentQuery, Result<CommentSummaryDto>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetSpecificCommentHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CommentSummaryDto>> Handle(
        GetSpecificCommentQuery request,
        CancellationToken cancellationToken)
    {
        string cacheKey = CacheKey.Comment(request.commentId);

        var cacheComment = await _redisService.GetAsync<CommentSummaryDto>(
            cacheKey,
            cancellationToken);

        if(cacheComment is not null)
            return Result.Ok(cacheComment);

        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        var dbComment = await commentCollection
            .Find(x => x.Id == request.commentId && !x.IsDeleted)
            .Project<Comment>(CommentProjection.Summary)
            .FirstOrDefaultAsync(cancellationToken);

        if(dbComment is null)
            return Result.Fail(AppError.NotFound("Comment not found"));

        var dto = _mapper.Map<CommentSummaryDto>(dbComment);

        await _redisService.SetAsync(
            cacheKey,
            dto,
            CacheKey.DefaultCommentTTL,
            cancellationToken);

        return Result.Ok(dto);
    }
}
