using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetSpecificNotificationHandler
    : IRequestHandler<GetSpecificNotificationQuery, Result<NotificationSummaryDto>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetSpecificNotificationHandler(
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

    public async Task<Result<NotificationSummaryDto>> Handle(
        GetSpecificNotificationQuery request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

        string cacheKey = CacheKey.Notification(request.notificationId);

        var cacheNotification = await _redisService.GetAsync<NotificationSummaryDto>(
            cacheKey,
            cancellationToken);

        if(cacheNotification is not null)
            return cacheNotification.ToAccountId == identity.Value
                ? Result.Ok(cacheNotification)
                : Result.Fail("Notification not found");

        var notificationCollection = _dbContext.GetCollection<Notification>(CollectionName.Notification);

        var dbNotification = await notificationCollection
            .Find(x => x.Id == request.notificationId && x.ToAccountId == identity.Value && !x.IsDeleted)
            .Project<Notification>(NotificationProjection.Summary)
            .FirstOrDefaultAsync(cancellationToken);

        if(dbNotification is null)
            return Result.Fail("Notification not found");

        var dto = _mapper.Map<NotificationSummaryDto>(dbNotification);

        await _redisService.SetAsync(
            cacheKey,
            dto,
            CacheKey.DefaultNotificationTTL,
            cancellationToken);

        return Result.Ok(dto);
    }
}
