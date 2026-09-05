using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetAllShopHandler
    : IRequestHandler<GetAllShopQuery, Result<CursorPageResult<ShopSummaryDto>>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetAllShopHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<ShopSummaryDto>>> Handle(
        GetAllShopQuery request,
        CancellationToken cancellationToken)
    {

        var shopCollection = _dbContext.GetCollection<Shop>(CollectionName.Shop);

        var filter = Builders<Shop>.Filter.Eq(x => x.IsDeleted, false);

        var dtoPage = await CachedCursorPaginator.PaginateAsync<Shop, ShopSummaryDto>(
            _redisService,
            _mapper,
            shopCollection,
            filter,
            ShopProjection.Summary,
            request.pageRequest,
            CollectionName.Shop,
            filterSignature: null,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
