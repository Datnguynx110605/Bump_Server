using System.Text.RegularExpressions;
using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Bson;
using MongoDB.Driver;

namespace app;

internal sealed class GetPostByKeywordHandler
    : IRequestHandler<GetPostByKeywordQuery, Result<CursorPageResult<PostFeedItemDto>>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetPostByKeywordHandler(
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

    public async Task<Result<CursorPageResult<PostFeedItemDto>>> Handle(
        GetPostByKeywordQuery request,
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

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var normalizedKeyword = TextNormalizer.RemoveDiacriticsAndLower(request.keyword);
        var escapedKeyword = Regex.Escape(normalizedKeyword);

        // No "i" option: TextNormalizer has already lowercased both the stored field and the
        // keyword, so case-insensitivity cannot change a match — and it is exactly what stops
        // MongoDB using the index on this field.
        var filter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Post>.Filter.Regex(x => x.CaptionNormalized, new BsonRegularExpression(escapedKeyword)));

        var dtoPage = await CachedCursorPaginator.PaginateAsync<Post, PostSummaryDto>(
            _redisService,
            _mapper,
            postCollection,
            filter,
            PostProjection.Summary,
            request.pageRequest,
            CollectionName.Post,
            filterSignature,
            cancellationToken,
            hidden,
            x => x.AccountId);

        return Result.Ok(await PostFeedEnricher.EnrichAsync(_dbContext, dtoPage, identity.Value, cancellationToken));
    }
}
