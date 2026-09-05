using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetAllCommentHandler
    : IRequestHandler<GetAllCommentQuery, Result<CursorPageResult<CommentSummaryDto>>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetAllCommentHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<CommentSummaryDto>>> Handle(
        GetAllCommentQuery request,
        CancellationToken cancellationToken)
    {
        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        var filter = Builders<Comment>.Filter.Eq(x => x.IsDeleted, false);

        var dtoPage = await CachedCursorPaginator.PaginateAsync<Comment, CommentSummaryDto>(
            _redisService,
            _mapper,
            commentCollection,
            filter,
            CommentProjection.Summary,
            request.pageRequest,
            CollectionName.Comment,
            filterSignature: null,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
