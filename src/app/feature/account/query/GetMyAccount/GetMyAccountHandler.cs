using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetMyAccountHandler
    : IRequestHandler<GetMyAccountQuery, Result<AccountSummaryDto>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetMyAccountHandler(
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

    public async Task<Result<AccountSummaryDto>> Handle(
        GetMyAccountQuery request, 
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

        string cacheKey = CacheKey.Account(identity.Value);

        AccountSummaryDto? cacheAccount = null;

        cacheAccount = await _redisService.GetAsync<AccountSummaryDto>(
            cacheKey,
            cancellationToken
        );

        if(cacheAccount is not null)
            return Result.Ok(cacheAccount);

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var projection = Builders<Account>.Projection
            .Include(x => x.Name)
            .Include(x => x.Avatars.Last())
            .Include(x => x.Bio)
            .Include(x => x.FollowerAmount)
            .Include(x => x.FollowingAmount);

        var dbAccount = await accountCollection
            .Find(x => x.Id == identity.Value && !x.IsDeleted)
            .Project<Account>(projection)
            .FirstOrDefaultAsync(cancellationToken);
            
        if(dbAccount is null)
            return Result.Fail("Unauthorized");

        var dto = _mapper.Map<AccountSummaryDto>(dbAccount);

        await _redisService.SetAsync(
            cacheKey,
            dto,
            CacheKey.DefaultAccountTTL,
            cancellationToken);

        return Result.Ok(dto);
    }
}
