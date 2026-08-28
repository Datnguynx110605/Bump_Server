using AutoMapper;
using domain;
using FluentResults;
using MediatR;

namespace app;

internal sealed class CreateReportHandler
    : IRequestHandler<CreateReportCommand, Result<ReportDto>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public CreateReportHandler(
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

    public async Task<Result<ReportDto>> Handle(
        CreateReportCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

        var reportResult = Report.Create(
            identity.Value,
            request.postId,
            request.commentId,
            request.accountId,
            request.reason);
        if(!reportResult.IsSuccess)
            return Result.Fail(reportResult.Error);

        Report report = reportResult.Value;

        var reportCollection = _dbContext.GetCollection<Report>(CollectionName.Report);

        await reportCollection.InsertOneAsync(
            report,
            cancellationToken : cancellationToken);

        var dto = _mapper.Map<ReportDto>(report);

        string cacheKey = CacheKey.Report(report.Id);

        await _redisService.SetAsync(
            cacheKey,
            dto,
            CacheKey.DefaultReportTTL,
            cancellationToken);

        return Result.Ok(dto);
    }
}
