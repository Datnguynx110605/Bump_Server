using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetMyFollowingHandler
    : IRequestHandler<GetMyFollowingQuery, Result<CursorPageResult<FollowingDto>>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetMyFollowingHandler(
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

    public async Task<Result<CursorPageResult<FollowingDto>>> Handle(
        GetMyFollowingQuery request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

        string filterSignature = $"account:{identity.Value}";
        string cacheKey = CacheKey.List($"{CollectionName.Account}:following", filterSignature, request.pageRequest);

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var parentFilter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, identity.Value),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false));

        var dtoPage = await CachedEmbeddedArrayPaginator.PaginateAsync<Account, Following, FollowingDto>(
            _redisService,
            _mapper,
            accountCollection,
            parentFilter,
            x => x.Followings,
            x => x.FollowedAt,
            x => x.AccountId,
            request.pageRequest,
            cacheKey,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
