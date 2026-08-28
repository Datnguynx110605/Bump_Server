using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetMostReactedPostHandler
    : IRequestHandler<GetMostReactedPostQuery, Result<CursorPageResult<PostSummaryDto>>>
{
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetMostReactedPostHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<PostSummaryDto>>> Handle(
        GetMostReactedPostQuery request,
        CancellationToken cancellationToken)
    {
        string filterSignature = "mostReacted";
        string cacheKey = CacheKey.List(CollectionName.Post, filterSignature, request.pageRequest);

        var cachedPage = await _redisService.GetAsync<CursorPageResult<PostSummaryDto>>(
            cacheKey,
            cancellationToken);

        if(cachedPage is not null)
            return Result.Ok(cachedPage);

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

        return Result.Ok(dtoPage);
    }
}
