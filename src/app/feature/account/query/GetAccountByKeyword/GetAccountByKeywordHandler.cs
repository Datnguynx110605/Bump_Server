using System.Text.RegularExpressions;
using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Bson;
using MongoDB.Driver;

namespace app;

internal sealed class GetAccountByKeywordHandler
    : IRequestHandler<GetAccountByKeywordQuery, Result<CursorPageResult<AccountSummaryDto>>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetAccountByKeywordHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<AccountSummaryDto>>> Handle(
        GetAccountByKeywordQuery request,
        CancellationToken cancellationToken)
    {
        string filterSignature = $"keyword:{request.keyword}";

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var escapedKeyword = Regex.Escape(request.keyword);

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Account>.Filter.Regex(x => x.Name.Value, new BsonRegularExpression(escapedKeyword, "i")));

        var dtoPage = await CachedCursorPaginator.PaginateAsync<Account, AccountSummaryDto>(
            _redisService,
            _mapper,
            accountCollection,
            filter,
            AccountProjection.Summary,
            request.pageRequest,
            CollectionName.Account,
            filterSignature,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
