using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetFollowerByAccountHandler
    : IRequestHandler<GetFollowerByAccountQuery, Result<CursorPageResult<FollowerDto>>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetFollowerByAccountHandler(
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

    public async Task<Result<CursorPageResult<FollowerDto>>> Handle(
        GetFollowerByAccountQuery request,
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

        string filterSignature = $"account:{request.accountId}";

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
            $"{CollectionName.Account}:follower",
            filterSignature,
            cancellationToken,
            hidden,
            x => x.AccountId);

        return Result.Ok(dtoPage);
    }
}
