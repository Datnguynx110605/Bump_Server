using app;
using domain;
using FluentResults;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace infra;

public sealed class NotificationService : INotificationService
{
    private readonly IMongoDbContext _dbContext;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly IRedisService _redisService;
    private readonly IApnsService _apnsService;
    private readonly ILogger<NotificationService> _logger;
    public NotificationService(
        IMongoDbContext dbContext,
        IHubContext<NotificationHub> hubContext,
        IRedisService redisService,
        IApnsService apnsService,
        ILogger<NotificationService> logger)
    {
        _dbContext = dbContext;
        _hubContext = hubContext;
        _redisService = redisService;
        _apnsService = apnsService;
        _logger = logger;
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
        // The row and the list invalidation above are already committed, so the recipient
        // will see this in their list either way. Letting a backplane failure throw here
        // unwound the whole queue message instead: the retry re-ran the caller, whose
        // conditional write then matched nothing and returned a failed Result, so the
        // message was acked and MarkAsSent below never ran. The push is the only part
        // nobody is waiting on, so it is the only part allowed to fail quietly.
        try
        {
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
        }
        catch(Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Realtime push failed for notification {NotificationId} to {ToAccountId}",
                notification.Id,
                toAccountId);
        }

        // The hub only reaches an app that is running and on screen: iOS drops the socket
        // seconds after backgrounding and kills the process on force-quit. APNs is the only
        // thing that can wake a client from those states, so it runs unconditionally rather
        // than as a fallback. Same fail-quiet policy, for the same reason as above.
        try
        {
            await _apnsService.SendAsync(
                toAccountId,
                notification.Title,
                notification.Content,
                type,
                targetId,
                notification.Id,
                cancellationToken);
        }
        catch(Exception ex)
        {
            _logger.LogWarning(
                ex,
                "APNs push failed for notification {NotificationId} to {ToAccountId}",
                notification.Id,
                toAccountId);
        }

        // Marks the attempt, not a confirmed delivery: SignalR has no receipt, and the
        // catch above means this is reached whether or not the push landed.
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
