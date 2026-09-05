using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetReportByTargetHandler
    : IRequestHandler<GetReportByTargetQuery, Result<CursorPageResult<ReportDto>>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetReportByTargetHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<ReportDto>>> Handle(
        GetReportByTargetQuery request,
        CancellationToken cancellationToken)
    {
        string filterSignature = $"{request.target.ToString().ToLowerInvariant()}:{request.targetId}";

        var reportCollection = _dbContext.GetCollection<Report>(CollectionName.Report);

        var filter = Builders<Report>.Filter.And(
            TargetFilter(request.target, request.targetId),
            Builders<Report>.Filter.Eq(x => x.IsDeleted, false));

        var dtoPage = await CachedCursorPaginator.PaginateAsync<Report, ReportDto>(
            _redisService,
            _mapper,
            reportCollection,
            filter,
            projection : null,
            request.pageRequest,
            CollectionName.Report,
            filterSignature,
            cancellationToken);

        return Result.Ok(dtoPage);
    }

    private static FilterDefinition<Report> TargetFilter(ReportTarget target, Guid targetId)
    {
        return target switch
        {
            ReportTarget.Post => Builders<Report>.Filter.Eq(x => x.PostId, targetId),
            ReportTarget.Comment => Builders<Report>.Filter.Eq(x => x.CommentId, targetId),
            ReportTarget.Account => Builders<Report>.Filter.Eq(x => x.AccountId, targetId),
            ReportTarget.Reporter => Builders<Report>.Filter.Eq(x => x.ReporterId, targetId),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
    }
}
