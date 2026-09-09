using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetMyBlockedAccountHandler
    : IRequestHandler<GetMyBlockedAccountQuery, Result<CursorPageResult<BlockDto>>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetMyBlockedAccountHandler(
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

    public async Task<Result<CursorPageResult<BlockDto>>> Handle(
        GetMyBlockedAccountQuery request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        string filterSignature = $"account:{identity.Value}";

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var parentFilter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, identity.Value),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false));

        var dtoPage = await CachedEmbeddedArrayPaginator.PaginateAsync<Account, Block, BlockDto>(
            _redisService,
            _mapper,
            accountCollection,
            parentFilter,
            x => x.Blocks,
            x => x.BlockedAt,
            x => x.AccountId,
            request.pageRequest,
            $"{CollectionName.Account}:block",
            filterSignature,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
