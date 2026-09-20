using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

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
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        // The target was never checked, so a report could name any GUID at all and the
        // administrator's queue filled with rows pointing at nothing. The validator has
        // already established that exactly one of the three is set.
        if(!await TargetExistsAsync(request, cancellationToken))
            return Result.Fail(AppError.NotFound("Report target not found"));

        var reportCollection = _dbContext.GetCollection<Report>(CollectionName.Report);

        // One live report per reporter per target. Nothing deduplicated before, and the
        // consequence reached further than a cluttered queue: FollowService.TryGrantTrustAsync
        // refuses to grant trust to any account carrying a live report, so one account could
        // file reports in a loop and hold another below the threshold indefinitely.
        var duplicateFilter = Builders<Report>.Filter.And(
            Builders<Report>.Filter.Eq(x => x.ReporterId, identity.Value),
            Builders<Report>.Filter.Eq(x => x.PostId, request.postId),
            Builders<Report>.Filter.Eq(x => x.CommentId, request.commentId),
            Builders<Report>.Filter.Eq(x => x.AccountId, request.accountId),
            Builders<Report>.Filter.Eq(x => x.IsDeleted, false));

        bool alreadyReported = await reportCollection
            .Find(duplicateFilter)
            .AnyAsync(cancellationToken);

        if(alreadyReported)
            return Result.Fail(AppError.Conflict("You have already reported this"));

        var reportResult = Report.Create(
            identity.Value,
            request.postId,
            request.commentId,
            request.accountId,
            request.reason);
        if(!reportResult.IsSuccess)
            return Result.Fail(reportResult.Error);

        Report report = reportResult.Value;

        await reportCollection.InsertOneAsync(
            report,
            cancellationToken : cancellationToken);

        var dto = _mapper.Map<ReportDto>(report);

        string cacheKey = CacheKey.Report(report.Id);

        await Task.WhenAll(
            _redisService.SetAsync(
                cacheKey,
                dto,
                CacheKey.DefaultReportTTL,
                cancellationToken),
            // The administrator listing is built from the report collection and was never
            // retired on write, so a new report stayed invisible to moderation for the page
            // cache's TTL.
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Report,
                filterSignature: null,
                cancellationToken));

        return Result.Ok(dto);
    }

    private async Task<bool> TargetExistsAsync(
        CreateReportCommand request,
        CancellationToken cancellationToken)
    {
        if(request.postId is not null)
            return await _dbContext.GetCollection<Post>(CollectionName.Post)
                .Find(x => x.Id == request.postId.Value && !x.IsDeleted)
                .AnyAsync(cancellationToken);

        if(request.commentId is not null)
            return await _dbContext.GetCollection<Comment>(CollectionName.Comment)
                .Find(x => x.Id == request.commentId.Value && !x.IsDeleted)
                .AnyAsync(cancellationToken);

        return await _dbContext.GetCollection<Account>(CollectionName.Account)
            .Find(x => x.Id == request.accountId!.Value && !x.IsDeleted)
            .AnyAsync(cancellationToken);
    }
}
