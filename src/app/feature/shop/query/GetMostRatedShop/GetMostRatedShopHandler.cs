using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetMostRatedShopHandler
    : IRequestHandler<GetMostRatedShopQuery, Result<CursorPageResult<ShopSummaryDto>>>
{
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetMostRatedShopHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<ShopSummaryDto>>> Handle(
        GetMostRatedShopQuery request,
        CancellationToken cancellationToken)
    {
        string filterSignature = "mostRated";

        var version = await ListCache.VersionAsync(
            _redisService,
            CollectionName.Shop,
            filterSignature,
            cancellationToken);

        string cacheKey = CacheKey.List(
            CollectionName.Shop,
            filterSignature,
            request.pageRequest,
            version);

        var cachedPage = await _redisService.GetAsync<CursorPageResult<ShopSummaryDto>>(
            cacheKey,
            cancellationToken);

        if(cachedPage is not null)
            return Result.Ok(cachedPage);

        var shopCollection = _dbContext.GetCollection<Shop>(CollectionName.Shop);

        var filter = Builders<Shop>.Filter.Eq(x => x.IsDeleted, false);

        var pageSize = Math.Clamp(request.pageRequest.PageSize, MinPageSize, MaxPageSize);

        var shops = await shopCollection
            .Find(filter)
            .SortByDescending(x => x.Rate.Value)
            .ThenByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Limit(pageSize)
            .Project<Shop>(ShopProjection.Summary)
            .ToListAsync(cancellationToken);

        var items = shops
            .Select(x => _mapper.Map<ShopSummaryDto>(x))
            .ToList();

        var dtoPage = new CursorPageResult<ShopSummaryDto>(items, null, false);

        await _redisService.SetAsync(
            cacheKey,
            dtoPage,
            CacheKey.DefaultListTTL,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
