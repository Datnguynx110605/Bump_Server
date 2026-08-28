using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetShopByNameHandler
    : IRequestHandler<GetShopByNameQuery, Result<CursorPageResult<ShopSummaryDto>>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetShopByNameHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<ShopSummaryDto>>> Handle(
        GetShopByNameQuery request,
        CancellationToken cancellationToken)
    {
        string filterSignature = $"name:{request.name}";
        string cacheKey = CacheKey.List(CollectionName.Shop, filterSignature, request.pageRequest);

        var shopCollection = _dbContext.GetCollection<Shop>(CollectionName.Shop);

        var filter = Builders<Shop>.Filter.And(
            Builders<Shop>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Shop>.Filter.Eq(x => x.Name, request.name));

        var dtoPage = await CachedCursorPaginator.PaginateAsync<Shop, ShopSummaryDto>(
            _redisService,
            _mapper,
            shopCollection,
            filter,
            ShopProjection.Summary,
            request.pageRequest,
            cacheKey,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
