using System.Text.RegularExpressions;
using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Bson;
using MongoDB.Driver;

namespace app;

internal sealed class GetPostByKeywordHandler
    : IRequestHandler<GetPostByKeywordQuery, Result<CursorPageResult<PostSummaryDto>>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetPostByKeywordHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<PostSummaryDto>>> Handle(
        GetPostByKeywordQuery request,
        CancellationToken cancellationToken)
    {
        string filterSignature = $"keyword:{request.keyword}";
        string cacheKey = CacheKey.List(CollectionName.Post, filterSignature, request.pageRequest);

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var escapedKeyword = Regex.Escape(request.keyword);

        var filter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Post>.Filter.Regex(x => x.Caption, new BsonRegularExpression(escapedKeyword, "i")));

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
