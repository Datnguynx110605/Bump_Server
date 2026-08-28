using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetFollowerByAccountHandler
    : IRequestHandler<GetFollowerByAccountQuery, Result<CursorPageResult<FollowerDto>>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetFollowerByAccountHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<FollowerDto>>> Handle(
        GetFollowerByAccountQuery request,
        CancellationToken cancellationToken)
    {
        string filterSignature = $"account:{request.accountId}";
        string cacheKey = CacheKey.List($"{CollectionName.Account}:follower", filterSignature, request.pageRequest);

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var parentFilter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, request.accountId),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false));

        var dtoPage = await CachedEmbeddedArrayPaginator.PaginateAsync<Account, Follower, FollowerDto>(
            _redisService,
            _mapper,
            accountCollection,
            parentFilter,
            x => x.Followers,
            x => x.FollowedAt,
            x => x.AccountId,
            request.pageRequest,
            cacheKey,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
