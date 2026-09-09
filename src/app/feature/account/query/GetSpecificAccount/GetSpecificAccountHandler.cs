using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetSpecificAccountHandler
    : IRequestHandler<GetSpecificAccountQuery, Result<AccountSummaryDto>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetSpecificAccountHandler(
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
        GetSpecificAccountQuery request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        var hidden = await BlockedAccountSet.ResolveAsync(
            _redisService,
            _dbContext,
            identity.Value,
            cancellationToken);

        // 404 rather than an empty body: a blocked profile must be indistinguishable from
        // one that does not exist, the same way ownership failures answer 404 here.
        if(hidden.Contains(request.accountId))
            return Result.Fail(AppError.NotFound("Account not found"));

        string cacheKey = CacheKey.Account(request.accountId);

        var cacheAccount = await _redisService.GetAsync<AccountSummaryDto>(
            cacheKey,
            cancellationToken);

        if(cacheAccount is not null)
            return Result.Ok(cacheAccount);

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var dbAccount = await accountCollection
            .Find(x => x.Id == request.accountId && !x.IsDeleted)
            .Project<Account>(AccountProjection.Summary)
            .FirstOrDefaultAsync(cancellationToken);

        if(dbAccount is null)
            return Result.Fail(AppError.NotFound("Account not found"));

        var dto = _mapper.Map<AccountSummaryDto>(dbAccount);

        await _redisService.SetAsync(
            cacheKey,
            dto,
            CacheKey.DefaultAccountTTL,
            cancellationToken);

        return Result.Ok(dto);
    }
}
