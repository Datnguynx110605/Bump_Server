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
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetAccountByKeywordHandler(
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
        GetAccountByKeywordQuery request,
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

        string filterSignature = $"keyword:{request.keyword}";

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        // Normalized like the post and shop searches. Name.Create already restricts stored
        // names to lowercase letters and digits, so without this a keyword carrying an
        // uppercase letter or a diacritic could never match anything.
        var normalizedKeyword = TextNormalizer.RemoveDiacriticsAndLower(request.keyword);
        var escapedKeyword = Regex.Escape(normalizedKeyword);

        // No "i" option: TextNormalizer has already lowercased both the stored field and the
        // keyword, so case-insensitivity cannot change a match — and it is exactly what stops
        // MongoDB using the index on this field.
        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Account>.Filter.Regex(x => x.Name.Value, new BsonRegularExpression(escapedKeyword)));

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
