using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetReactionByCommentHandler
    : IRequestHandler<GetReactionByCommentQuery, Result<CursorPageResult<ReactionDto>>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetReactionByCommentHandler(
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
        GetReactionByCommentQuery request,
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

        string filterSignature = $"comment:{request.commentId}";

        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        var parentFilter = Builders<Comment>.Filter.And(
            Builders<Comment>.Filter.Eq(x => x.Id, request.commentId),
            Builders<Comment>.Filter.Eq(x => x.IsDeleted, false));

        var dtoPage = await CachedEmbeddedArrayPaginator.PaginateAsync<Comment, Reaction, ReactionDto>(
            _redisService,
            _mapper,
            commentCollection,
            parentFilter,
            x => x.Reactions,
            x => x.ReactedAt,
            x => x.AccountId,
            request.pageRequest,
            $"{CollectionName.Comment}:reaction",
            filterSignature,
            cancellationToken,
            hidden,
            x => x.AccountId);

        return Result.Ok(dtoPage);
    }
}
