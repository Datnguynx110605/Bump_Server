using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetMyCommentHistoryHandler
    : IRequestHandler<GetMyCommentHistoryQuery, Result<CursorPageResult<CommentSummaryDto>>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetMyCommentHistoryHandler(
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

    public async Task<Result<CursorPageResult<CommentSummaryDto>>> Handle(
        GetMyCommentHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        string filterSignature = $"account:{identity.Value}";

        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        var filter = Builders<Comment>.Filter.And(
            Builders<Comment>.Filter.Eq(x => x.AccountId, identity.Value),
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
