using app;
using AutoMapper;
using domain;
using FluentResults;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using StackExchange.Redis;

namespace infra;

public sealed class NotificationService : INotificationService
{
    private readonly IMongoDbContext _dbContext;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly IRedisService _redisService;
    private readonly IMapper _mapper;
    public NotificationService(
        IMongoDbContext dbContext,
        IHubContext<NotificationHub> hubContext,
        IConnectionMultiplexer redis,
        IRedisService redisService,
        IMapper mapper)
    {
        _dbContext = dbContext;
        _hubContext = hubContext;
        _redis = redis;
        _redisService = redisService;
        _mapper = mapper;
    }

    public async Task<Result> CreateAsync(
        Guid toAccountId,
        string title,
        string content,
        CancellationToken cancellationToken)
    {
        Notification notification = new Notification(
            toAccountId,
            title,
            content);

        var notificationCollection = _dbContext.GetCollection<Notification>(CollectionName.Notification);

        await notificationCollection.InsertOneAsync(
            notification,
            cancellationToken : cancellationToken);

        var summaryDto = _mapper.Map<NotificationSummaryDto>(notification);

        await _redisService.SetAsync(
            CacheKey.Notification(notification.Id),
            summaryDto,
            CacheKey.DefaultNotificationTTL,
            cancellationToken);

        string listCacheKey = CacheKey.List(
            CollectionName.Notification,
            $"account:{toAccountId}",
            new CursorPageRequest(null));

        await _redisService.RemoveAsync(
            listCacheKey,
            cancellationToken);

        var onlineCount = await _redis.GetDatabase().StringGetAsync(NotificationHub.OnlineKey(toAccountId.ToString()));
        bool isOnline = onlineCount.HasValue && (long)onlineCount > 0;

        if(!isOnline)
            return Result.Ok();

        await _hubContext.Clients
            .User(toAccountId.ToString())
            .SendAsync(
                "ReceiveNotification",
                new
                {
                    notification.Id,
                    notification.Title,
                    notification.Content,
                    notification.CreatedAt
                },
                cancellationToken);

        var result = notification.MarkAsSent();
        if(!result.IsSuccess)
            return Result.Fail(result.Error);

        var filter = Builders<Notification>.Filter.Eq(x => x.Id, notification.Id);

        var update = Builders<Notification>.Update
            .Set(x => x.IsSent, notification.IsSent)
            .Set(x => x.UpdatedAt, notification.UpdatedAt);

        await notificationCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken : cancellationToken);

        await _redisService.RemoveAsync(
            CacheKey.Notification(notification.Id),
            cancellationToken);

        return Result.Ok();
    }
}
