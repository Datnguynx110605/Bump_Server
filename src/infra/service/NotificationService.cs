using app;
using domain;
using FluentResults;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;

namespace infra;

public sealed class NotificationService : INotificationService
{
    private readonly IMongoDbContext _dbContext;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly IRedisService _redisService;
    public NotificationService(
        IMongoDbContext dbContext,
        IHubContext<NotificationHub> hubContext,
        IRedisService redisService)
    {
        _dbContext = dbContext;
        _hubContext = hubContext;
        _redisService = redisService;
    }

    public async Task<Result> CreateAsync(
        Guid toAccountId,
        string title,
        string content,
        NotificationType type,
        Guid targetId,
        Guid actorAccountId,
        Avatar? actorAvatar,
        CancellationToken cancellationToken)
    {
        var notificationResult = Notification.Create(
            toAccountId,
            title,
            content,
            type,
            targetId,
            actorAccountId,
            actorAvatar);

        if(!notificationResult.IsSuccess)
            return Result.Fail(notificationResult.Error);

        Notification notification = notificationResult.Value!;

        var notificationCollection = _dbContext.GetCollection<Notification>(CollectionName.Notification);

        await notificationCollection.InsertOneAsync(
            notification,
            cancellationToken : cancellationToken);

        // Nothing caches the notification here: delivery is now unconditional, so the
        // MarkAsSent write below always invalidates the entry again. Reads populate it.
        await ListCache.InvalidateAsync(
            _redisService,
            CollectionName.Notification,
            $"account:{toAccountId}",
            cancellationToken);

        // No online check: with the Redis backplane the hub routes to whichever replica
        // holds the connection, and sending to a disconnected user is a no-op. The old
        // check read a global counter but sent only to locally-held connections, so any
        // multi-replica deploy dropped the notification and still marked it sent.
        await _hubContext.Clients
            .User(toAccountId.ToString())
            .SendAsync(
                "ReceiveNotification",
                new
                {
                    notification.Id,
                    notification.Title,
                    notification.Content,
                    notification.CreatedAt,
                    notification.Type,
                    notification.TargetId,
                    notification.ActorAccountId,
                    notification.ActorAvatar
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
