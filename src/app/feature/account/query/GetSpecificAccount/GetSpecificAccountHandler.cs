using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetSpecificAccountHandler
    : IRequestHandler<GetSpecificAccountQuery, Result<AccountSummaryDto>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetSpecificAccountHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<AccountSummaryDto>> Handle(
        GetSpecificAccountQuery request,
        CancellationToken cancellationToken)
    {
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
