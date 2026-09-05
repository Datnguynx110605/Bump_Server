using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetMySavedPostsHandler
    : IRequestHandler<GetMySavedPostsQuery, Result<CursorPageResult<PostSummaryDto>>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetMySavedPostsHandler(
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

    public async Task<Result<CursorPageResult<PostSummaryDto>>> Handle(
        GetMySavedPostsQuery request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        var version = await ListCache.VersionAsync(
            _redisService,
            $"{CollectionName.Account}:savepost",
            $"account:{identity.Value}",
            cancellationToken);

        string cacheKey = CacheKey.List(
            $"{CollectionName.Account}:savepost",
            $"account:{identity.Value}",
            request.pageRequest,
            version);

        var cachedPage = await _redisService.GetAsync<CursorPageResult<PostSummaryDto>>(
            cacheKey,
            cancellationToken);
        if(cachedPage is not null)
            return Result.Ok(cachedPage);

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var parentFilter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, identity.Value),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false));

        var idPage = await EmbeddedArrayPaginator.PaginateAsync<Account, SavedPost>(
            accountCollection,
            parentFilter,
            x => x.SavePosts,
            x => x.SavedAt,
            x => x.PostId,
            request.pageRequest,
            cancellationToken);

        var postIds = idPage.Items.Select(x => x.PostId).ToList();

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var posts = await postCollection
            .Find(x => postIds.Contains(x.Id) && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        var postsById = posts.ToDictionary(x => x.Id);

        var items = postIds
            .Where(id => postsById.ContainsKey(id))
            .Select(id => _mapper.Map<PostSummaryDto>(postsById[id]))
            .ToList();

        var dtoPage = new CursorPageResult<PostSummaryDto>(items, idPage.NextCursor, idPage.HasMore);

        await _redisService.SetAsync(
            cacheKey,
            dtoPage,
            CacheKey.DefaultListTTL,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
