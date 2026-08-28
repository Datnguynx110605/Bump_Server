using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetSpecificShopHandler
    : IRequestHandler<GetSpecificShopQuery, Result<ShopDto>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetSpecificShopHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<ShopDto>> Handle(
        GetSpecificShopQuery request,
        CancellationToken cancellationToken)
    {
        string cacheKey = CacheKey.Shop(request.shopId);

        var cacheShop = await _redisService.GetAsync<ShopDto>(
            cacheKey,
            cancellationToken);

        if(cacheShop is not null)
            return Result.Ok(cacheShop);

        var shopCollection = _dbContext.GetCollection<Shop>(CollectionName.Shop);

        var dbShop = await shopCollection
            .Find(x => x.Id == request.shopId && !x.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if(dbShop is null)
            return Result.Fail("Shop not found");

        var dto = _mapper.Map<ShopDto>(dbShop);

        await _redisService.SetAsync(
            cacheKey,
            dto,
            CacheKey.DefaultShopTTL,
            cancellationToken);

        return Result.Ok(dto);
    }
}
