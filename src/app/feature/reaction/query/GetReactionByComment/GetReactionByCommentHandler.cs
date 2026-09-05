using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetReactionByCommentHandler
    : IRequestHandler<GetReactionByCommentQuery, Result<CursorPageResult<ReactionDto>>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetReactionByCommentHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<ReactionDto>>> Handle(
        GetReactionByCommentQuery request,
        CancellationToken cancellationToken)
    {
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
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
