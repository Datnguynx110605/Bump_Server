using System.Text.RegularExpressions;
using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Bson;
using MongoDB.Driver;

namespace app;

internal sealed class GetShopByKeywordHandler
    : IRequestHandler<GetShopByKeywordQuery, Result<CursorPageResult<ShopSummaryDto>>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetShopByKeywordHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<ShopSummaryDto>>> Handle(
        GetShopByKeywordQuery request,
        CancellationToken cancellationToken)
    {
        string filterSignature = $"keyword:{request.keyword}";
        string cacheKey = CacheKey.List(CollectionName.Shop, filterSignature, request.pageRequest);

        var shopCollection = _dbContext.GetCollection<Shop>(CollectionName.Shop);

        var normalizedKeyword = TextNormalizer.RemoveDiacriticsAndLower(request.keyword);
        var escapedKeyword = Regex.Escape(normalizedKeyword);

        var filter = Builders<Shop>.Filter.And(
            Builders<Shop>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Shop>.Filter.Or(
                Builders<Shop>.Filter.Regex(x => x.NameNormalized, new BsonRegularExpression(escapedKeyword, "i")),
                Builders<Shop>.Filter.Regex(x => x.AddressNormalized, new BsonRegularExpression(escapedKeyword, "i"))));

        var dtoPage = await CachedCursorPaginator.PaginateAsync<Shop, ShopSummaryDto>(
            _redisService,
            _mapper,
            shopCollection,
            filter,
            ShopProjection.Summary,
            request.pageRequest,
            cacheKey,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
