using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetSpecificCommentHandler
    : IRequestHandler<GetSpecificCommentQuery, Result<CommentSummaryDto>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetSpecificCommentHandler(
        ICurrentIdentityService identityService,
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _identityService = identityService;
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CommentSummaryDto>> Handle(
        GetSpecificCommentQuery request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        var hidden = await BlockedAccountSet.ResolveAsync(
            _redisService,
            _dbContext,
            identity.Value,
            cancellationToken);

        string cacheKey = CacheKey.Comment(request.commentId);

        var cacheComment = await _redisService.GetAsync<CommentSummaryDto>(
            cacheKey,
            cancellationToken);

        // The entity cache is shared across viewers, so the check cannot live inside the
        // cached value and has to sit on both exits.
        if(cacheComment is not null)
            return hidden.Contains(cacheComment.AccountId)
                ? Result.Fail(AppError.NotFound("Comment not found"))
                : Result.Ok(cacheComment);

        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        var dbComment = await commentCollection
            .Find(x => x.Id == request.commentId && !x.IsDeleted)
            .Project<Comment>(CommentProjection.Summary)
            .FirstOrDefaultAsync(cancellationToken);

        if(dbComment is null)
            return Result.Fail(AppError.NotFound("Comment not found"));

        if(hidden.Contains(dbComment.AccountId))
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
