using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetShopByAddressHandler
    : IRequestHandler<GetShopByAddressQuery, Result<CursorPageResult<ShopSummaryDto>>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetShopByAddressHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<ShopSummaryDto>>> Handle(
        GetShopByAddressQuery request,
        CancellationToken cancellationToken)
    {
        string filterSignature = $"address:{request.address}";

        var shopCollection = _dbContext.GetCollection<Shop>(CollectionName.Shop);

        var filter = Builders<Shop>.Filter.And(
            Builders<Shop>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Shop>.Filter.Eq(x => x.Address, request.address));

        var dtoPage = await CachedCursorPaginator.PaginateAsync<Shop, ShopSummaryDto>(
            _redisService,
            _mapper,
            shopCollection,
            filter,
            ShopProjection.Summary,
            request.pageRequest,
            CollectionName.Shop,
            filterSignature,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
