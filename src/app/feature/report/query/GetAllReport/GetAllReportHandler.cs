using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetAllReportHandler
    : IRequestHandler<GetAllReportQuery, Result<CursorPageResult<ReportDto>>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetAllReportHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<ReportDto>>> Handle(
        GetAllReportQuery request,
        CancellationToken cancellationToken)
    {

        var reportCollection = _dbContext.GetCollection<Report>(CollectionName.Report);

        var filter = Builders<Report>.Filter.Eq(x => x.IsDeleted, false);

        var dtoPage = await CachedCursorPaginator.PaginateAsync<Report, ReportDto>(
            _redisService,
            _mapper,
            reportCollection,
            filter,
            projection : null,
            request.pageRequest,
            CollectionName.Report,
            filterSignature: null,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
