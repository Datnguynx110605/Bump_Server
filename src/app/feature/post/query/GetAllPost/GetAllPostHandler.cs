using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetAllPostHandler
    : IRequestHandler<GetAllPostQuery, Result<CursorPageResult<PostSummaryDto>>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetAllPostHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<PostSummaryDto>>> Handle(
        GetAllPostQuery request,
        CancellationToken cancellationToken)
    {
        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var filter = Builders<Post>.Filter.Eq(x => x.IsDeleted, false);

        var dtoPage = await CachedCursorPaginator.PaginateAsync<Post, PostSummaryDto>(
            _redisService,
            _mapper,
            postCollection,
            filter,
            PostProjection.Summary,
            request.pageRequest,
            CollectionName.Post,
            filterSignature: null,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
