using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetPostByAccountHandler
    : IRequestHandler<GetPostByAccountQuery, Result<CursorPageResult<PostSummaryDto>>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetPostByAccountHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<PostSummaryDto>>> Handle(
        GetPostByAccountQuery request,
        CancellationToken cancellationToken)
    {
        string filterSignature = $"account:{request.accountId}";
        string cacheKey = CacheKey.List(CollectionName.Post, filterSignature, request.pageRequest);

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var filter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Eq(x => x.AccountId, request.accountId),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false));

        var dtoPage = await CachedCursorPaginator.PaginateAsync<Post, PostSummaryDto>(
            _redisService,
            _mapper,
            postCollection,
            filter,
            PostProjection.Summary,
            request.pageRequest,
            cacheKey,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
