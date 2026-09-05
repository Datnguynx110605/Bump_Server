using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetSpecificReportHandler
    : IRequestHandler<GetSpecificReportQuery, Result<ReportDto>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetSpecificReportHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<ReportDto>> Handle(
        GetSpecificReportQuery request,
        CancellationToken cancellationToken)
    {
        string cacheKey = CacheKey.Report(request.reportId);

        var cacheReport = await _redisService.GetAsync<ReportDto>(
            cacheKey,
            cancellationToken);

        if(cacheReport is not null)
            return Result.Ok(cacheReport);

        var reportCollection = _dbContext.GetCollection<Report>(CollectionName.Report);

        var dbReport = await reportCollection
            .Find(x => x.Id == request.reportId && !x.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if(dbReport is null)
            return Result.Fail(AppError.NotFound("Report not found"));

        var dto = _mapper.Map<ReportDto>(dbReport);

        await _redisService.SetAsync(
            cacheKey,
            dto,
            CacheKey.DefaultReportTTL,
            cancellationToken);

        return Result.Ok(dto);
    }
}
