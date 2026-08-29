using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetMyNotificationHandler
    : IRequestHandler<GetMyNotificationQuery, Result<CursorPageResult<NotificationSummaryDto>>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetMyNotificationHandler(
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

    public async Task<Result<CursorPageResult<NotificationSummaryDto>>> Handle(
        GetMyNotificationQuery request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

        string filterSignature = $"account:{identity.Value}";
        string cacheKey = CacheKey.List(CollectionName.Notification, filterSignature, request.pageRequest);

        var notificationCollection = _dbContext.GetCollection<Notification>(CollectionName.Notification);

        var filter = Builders<Notification>.Filter.And(
            Builders<Notification>.Filter.Eq(x => x.ToAccountId, identity.Value),
            Builders<Notification>.Filter.Eq(x => x.IsDeleted, false));

        var dtoPage = await CachedCursorPaginator.PaginateAsync<Notification, NotificationSummaryDto>(
            _redisService,
            _mapper,
            notificationCollection,
            filter,
            NotificationProjection.Summary,
            request.pageRequest,
            cacheKey,
            cancellationToken);

        return Result.Ok(dtoPage);
    }
}
