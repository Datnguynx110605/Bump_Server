using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetMostReactedPostHandler
    : IRequestHandler<GetMostReactedPostQuery, Result<CursorPageResult<PostFeedItemDto>>>
{
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetMostReactedPostHandler(
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

    public async Task<Result<CursorPageResult<PostFeedItemDto>>> Handle(
        GetMostReactedPostQuery request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        string filterSignature = "mostReacted";

        var version = await ListCache.VersionAsync(
            _redisService,
            CollectionName.Post,
            filterSignature,
            cancellationToken);

        string cacheKey = CacheKey.List(
            CollectionName.Post,
            filterSignature,
            request.pageRequest,
            version);

        var cachedPage = await _redisService.GetAsync<CursorPageResult<PostSummaryDto>>(
            cacheKey,
            cancellationToken);

        if(cachedPage is not null)
            return Result.Ok(await PostFeedEnricher.EnrichAsync(_dbContext, cachedPage, identity.Value, cancellationToken));

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var filter = Builders<Post>.Filter.Eq(x => x.IsDeleted, false);

        var pageSize = Math.Clamp(request.pageRequest.PageSize, MinPageSize, MaxPageSize);

        var posts = await postCollection
            .Find(filter)
            .SortByDescending(x => x.ReactionAmount.Value)
            .ThenByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Limit(pageSize)
            .Project<Post>(PostProjection.Summary)
            .ToListAsync(cancellationToken);

        var items = posts
            .Select(x => _mapper.Map<PostSummaryDto>(x))
            .ToList();

        var dtoPage = new CursorPageResult<PostSummaryDto>(items, null, false);

        await _redisService.SetAsync(
            cacheKey,
            dtoPage,
            CacheKey.DefaultListTTL,
            cancellationToken);

        return Result.Ok(await PostFeedEnricher.EnrichAsync(_dbContext, dtoPage, identity.Value, cancellationToken));
    }
}
