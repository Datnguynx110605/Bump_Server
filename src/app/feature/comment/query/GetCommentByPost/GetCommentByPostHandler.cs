using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetCommentByPostHandler
    : IRequestHandler<GetCommentByPostQuery, Result<CursorPageResult<CommentSummaryDto>>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetCommentByPostHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<CommentSummaryDto>>> Handle(
        GetCommentByPostQuery request,
        CancellationToken cancellationToken)
    {
        string filterSignature = $"post:{request.postId}";

        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        var filter = Builders<Comment>.Filter.And(
            Builders<Comment>.Filter.Eq(x => x.PostId, request.postId),
            Builders<Comment>.Filter.Eq(x => x.IsDeleted, false));

        var dtoPage = await CachedCursorPaginator.PaginateAsync<Comment, CommentSummaryDto>(
            _redisService,
            _mapper,
            commentCollection,
            filter,
            CommentProjection.Summary,
            request.pageRequest,
            CollectionName.Comment,
            filterSignature,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
