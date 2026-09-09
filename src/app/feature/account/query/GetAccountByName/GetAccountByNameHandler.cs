using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetAccountByNameHandler
    : IRequestHandler<GetAccountByNameQuery, Result<CursorPageResult<AccountSummaryDto>>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetAccountByNameHandler(
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

    public async Task<Result<CursorPageResult<AccountSummaryDto>>> Handle(
        GetAccountByNameQuery request,
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

        string filterSignature = $"name:{request.name.Value}";

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Account>.Filter.Eq(x => x.Name, request.name));

        var dtoPage = await CachedCursorPaginator.PaginateAsync<Account, AccountSummaryDto>(
            _redisService,
            _mapper,
            accountCollection,
            filter,
            AccountProjection.Summary,
            request.pageRequest,
            CollectionName.Account,
            filterSignature,
            cancellationToken,
            hidden,
            x => x.Id);

        return Result.Ok(dtoPage);
    }
}
