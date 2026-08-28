using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetAllAccountHandler
    : IRequestHandler<GetAllAccountQuery, Result<CursorPageResult<AccountSummaryDto>>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetAllAccountHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<AccountSummaryDto>>> Handle(
        GetAllAccountQuery request,
        CancellationToken cancellationToken)
    {
        string cacheKey = CacheKey.List(CollectionName.Account, filterSignature: null, request.pageRequest);

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var filter = Builders<Account>.Filter.Eq(x => x.IsDeleted, false);

        var dtoPage = await CachedCursorPaginator.PaginateAsync<Account, AccountSummaryDto>(
            _redisService,
            _mapper,
            accountCollection,
            filter,
            AccountProjection.Summary,
            request.pageRequest,
            cacheKey,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
