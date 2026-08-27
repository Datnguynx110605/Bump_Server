using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetAccountByNameHandler
    : IRequestHandler<GetAccountByNameQuery, Result<CursorPageResult<AccountSummaryDto>>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetAccountByNameHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<AccountSummaryDto>>> Handle(
        GetAccountByNameQuery request,
        CancellationToken cancellationToken)
    {
        string filterSignature = $"name:{request.name.Value}";
        string cacheKey = CacheKey.List(CollectionName.Account, filterSignature, request.pageRequest);

        var cachedPage = await _redisService.GetAsync<CursorPageResult<AccountSummaryDto>>(
            cacheKey,
            cancellationToken);

        if(cachedPage is not null)
            return Result.Ok(cachedPage);

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Account>.Filter.Eq(x => x.Name, request.name));

        var projection = Builders<Account>.Projection
            .Include(x => x.Name)
            .Include(x => x.Avatars.Last())
            .Include(x => x.Bio)
            .Include(x => x.FollowerAmount)
            .Include(x => x.FollowingAmount);

        var accountPage = await CursorPaginator.PaginateAsync(
            accountCollection,
            filter,
            projection,
            request.pageRequest,
            cancellationToken);

        var items = accountPage.Items
            .Select(x => _mapper.Map<AccountSummaryDto>(x))
            .ToList();

        var dtoPage = new CursorPageResult<AccountSummaryDto>(
            items,
            accountPage.NextCursor,
            accountPage.HasMore);

        await _redisService.SetAsync(
            cacheKey,
            dtoPage,
            CacheKey.DefaultListTTL,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
