using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetSpecificPostHandler
    : IRequestHandler<GetSpecificPostQuery, Result<PostFeedItemDto>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetSpecificPostHandler(
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

    public async Task<Result<PostFeedItemDto>> Handle(
        GetSpecificPostQuery request,
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

        string cacheKey = CacheKey.Post(request.postId);

        var cachePost = await _redisService.GetAsync<PostSummaryDto>(
            cacheKey,
            cancellationToken);

        // The entity cache is shared across viewers, so the check cannot live inside the
        // cached value and has to sit on both exits.
        if(cachePost is not null)
            return hidden.Contains(cachePost.AccountId)
                ? Result.Fail(AppError.NotFound("Post not found"))
                : Result.Ok(await PostFeedEnricher.EnrichAsync(_dbContext, cachePost, identity.Value, cancellationToken));

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var dbPost = await postCollection
            .Find(x => x.Id == request.postId && !x.IsDeleted)
            .Project<Post>(PostProjection.Summary)
            .FirstOrDefaultAsync(cancellationToken);

        if(dbPost is null)
            return Result.Fail(AppError.NotFound("Post not found"));

        if(hidden.Contains(dbPost.AccountId))
            return Result.Fail(AppError.NotFound("Post not found"));

        var dto = _mapper.Map<PostSummaryDto>(dbPost);

        await _redisService.SetAsync(
            cacheKey,
            dto,
            CacheKey.DefaultPostTTL,
            cancellationToken);

        return Result.Ok(await PostFeedEnricher.EnrichAsync(_dbContext, dto, identity.Value, cancellationToken));
    }
}
