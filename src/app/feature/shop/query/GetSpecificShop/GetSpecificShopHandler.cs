using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetSpecificShopHandler
    : IRequestHandler<GetSpecificShopQuery, Result<ShopDetailDto>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetSpecificShopHandler(
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

    public async Task<Result<ShopDetailDto>> Handle(
        GetSpecificShopQuery request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        string cacheKey = CacheKey.Shop(request.shopId);

        var cacheShop = await _redisService.GetAsync<ShopDto>(
            cacheKey,
            cancellationToken);

        ShopDto dto;
        if(cacheShop is not null)
        {
            dto = cacheShop;
        }
        else
        {
            var shopCollection = _dbContext.GetCollection<Shop>(CollectionName.Shop);

            var dbShop = await shopCollection
                .Find(x => x.Id == request.shopId && !x.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if(dbShop is null)
                return Result.Fail(AppError.NotFound("Shop not found"));

            dto = _mapper.Map<ShopDto>(dbShop);

            await _redisService.SetAsync(
                cacheKey,
                dto,
                CacheKey.DefaultShopTTL,
                cancellationToken);
        }

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);
        var isSaved = await SaveMembership.HasSavedShopAsync(accountCollection, identity.Value, request.shopId, cancellationToken);

        return Result.Ok(new ShopDetailDto(dto, isSaved));
    }
}
