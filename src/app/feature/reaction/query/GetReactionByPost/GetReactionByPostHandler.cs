using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetReactionByPostHandler
    : IRequestHandler<GetReactionByPostQuery, Result<CursorPageResult<ReactionDto>>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetReactionByPostHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<ReactionDto>>> Handle(
        GetReactionByPostQuery request,
        CancellationToken cancellationToken)
    {
        string filterSignature = $"post:{request.postId}";
        string cacheKey = CacheKey.List($"{CollectionName.Post}:reaction", filterSignature, request.pageRequest);

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var parentFilter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Eq(x => x.Id, request.postId),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false));

        var dtoPage = await CachedEmbeddedArrayPaginator.PaginateAsync<Post, Reaction, ReactionDto>(
            _redisService,
            _mapper,
            postCollection,
            parentFilter,
            x => x.Reactions,
            x => x.ReactedAt,
            x => x.AccountId,
            request.pageRequest,
            cacheKey,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
