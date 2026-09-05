using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetMySavedShopsHandler
    : IRequestHandler<GetMySavedShopsQuery, Result<CursorPageResult<ShopSummaryDto>>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetMySavedShopsHandler(
        ICurrentIdentityService identityService,
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _identityService = identityService;
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<ShopSummaryDto>>> Handle(
        GetMySavedShopsQuery request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        var version = await ListCache.VersionAsync(
            _redisService,
            $"{CollectionName.Account}:saveshop",
            $"account:{identity.Value}",
            cancellationToken);

        string cacheKey = CacheKey.List(
            $"{CollectionName.Account}:saveshop",
            $"account:{identity.Value}",
            request.pageRequest,
            version);

        var cachedPage = await _redisService.GetAsync<CursorPageResult<ShopSummaryDto>>(
            cacheKey,
            cancellationToken);
        if(cachedPage is not null)
            return Result.Ok(cachedPage);

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var parentFilter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, identity.Value),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false));

        var idPage = await EmbeddedArrayPaginator.PaginateAsync<Account, SavedShop>(
            accountCollection,
            parentFilter,
            x => x.SaveShops,
            x => x.SavedAt,
            x => x.ShopId,
            request.pageRequest,
            cancellationToken);

        var shopIds = idPage.Items.Select(x => x.ShopId).ToList();

        var shopCollection = _dbContext.GetCollection<Shop>(CollectionName.Shop);

        var shops = await shopCollection
            .Find(x => shopIds.Contains(x.Id) && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        var shopsById = shops.ToDictionary(x => x.Id);

        var items = shopIds
            .Where(id => shopsById.ContainsKey(id))
            .Select(id => _mapper.Map<ShopSummaryDto>(shopsById[id]))
            .ToList();

        var dtoPage = new CursorPageResult<ShopSummaryDto>(items, idPage.NextCursor, idPage.HasMore);

        await _redisService.SetAsync(
            cacheKey,
            dtoPage,
            CacheKey.DefaultListTTL,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
