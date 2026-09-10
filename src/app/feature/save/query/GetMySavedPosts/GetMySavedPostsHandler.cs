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

        var hidden = await BlockedAccountSet.ResolveAsync(
            _redisService,
            _dbContext,
            identity.Value,
            cancellationToken);

        // The saved list is the viewer's own, but the posts on it are other people's, so
        // it filters like every other read of someone else's content. Without this a post
        // saved before its author was blocked stays on the list and then answers 404 from
        // #22 GetSpecificPost when opened.
        string filterSignature = $"account:{identity.Value}";
        string? pageSignature = BlockedAccountSet.Signature(filterSignature, hidden);

        // Counter unsuffixed, page suffixed — the same split the cached paginators make.
        // SaveService bumps account:savepost/account:{me}, so reading the counter under a
        // :hidden: suffix would mean a save never invalidating the saved list of anyone
        // who has blocked someone.
        var version = await ListCache.VersionAsync(
            _redisService,
            $"{CollectionName.Account}:savepost",
            filterSignature,
            cancellationToken);

        string cacheKey = CacheKey.List(
            $"{CollectionName.Account}:savepost",
            pageSignature,
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

        var postFilter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.In(x => x.Id, postIds),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false));

        // Unlike the other filtered reads, the $nin cannot ride the paginating query:
        // SavedPost holds only PostId and SavedAt, so the author is not known until this
        // second stage. That makes this the one path where hiding shortens a page — the
        // same way a deleted post already does here, and for the same structural reason.
        // Denormalizing the author onto SavedPost is what would move it into the $match,
        // and that needs a backfill for every existing save.
        if(hidden.Count > 0)
            postFilter = Builders<Post>.Filter.And(
                postFilter,
                Builders<Post>.Filter.Nin(x => x.AccountId, hidden));

        // Projected, not whole documents: an unprojected read drags each post's entire
        // Reactions array over the wire, so one page of saved popular posts could pull
        // megabytes to render a summary.
        var posts = await postCollection
            .Find(postFilter)
            .Project<Post>(PostProjection.Summary)
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
