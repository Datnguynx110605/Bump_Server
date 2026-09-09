using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetReactionByPostHandler
    : IRequestHandler<GetReactionByPostQuery, Result<CursorPageResult<ReactionDto>>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetReactionByPostHandler(
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

    public async Task<Result<CursorPageResult<ReactionDto>>> Handle(
        GetReactionByPostQuery request,
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

        string filterSignature = $"post:{request.postId}";

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var parentFilter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Eq(x => x.Id, request.postId),
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false));

        var dtoPage = await CachedEmbeddedArrayPaginator.PaginateAsync<Post, Reaction, ReactionDto>(
            _redisService,
            _mapper,
            postCollection,
            parentFilter,
            x => x.Reactions,
            x => x.ReactedAt,
            x => x.AccountId,
            request.pageRequest,
            $"{CollectionName.Post}:reaction",
            filterSignature,
            cancellationToken,
            hidden,
            x => x.AccountId);

        return Result.Ok(dtoPage);
    }
}
