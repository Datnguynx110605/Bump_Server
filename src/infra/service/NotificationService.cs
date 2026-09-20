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

        // Marked before the insert rather than by a second write after the pushes. The flag
        // records that delivery was attempted, not that it landed — the catch blocks below
        // mean it was set either way — so the follow-up write had nothing to learn from the
        // attempt, and it cost every notification a second Mongo round trip plus a Redis
        // delete against a key this method never writes. Notifications are the highest
        // frequency write in the system, which is what made one redundant round trip each
        // worth removing.
        var sentResult = notification.MarkAsSent();
        if(!sentResult.IsSuccess)
            return Result.Fail(sentResult.Error);

        var notificationCollection = _dbContext.GetCollection<Notification>(CollectionName.Notification);

        await notificationCollection.InsertOneAsync(
            notification,
            cancellationToken : cancellationToken);

        // The recipient's list is retired, but the notification itself is not written to the
        // entity cache here — GetSpecificNotification populates that on the first read, and
        // most notifications are never opened individually.
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
        // message was acked and the notification was never delivered at all. The push is the
        // only part nobody is waiting on, so it is the only part allowed to fail quietly.
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

        return Result.Ok();
    }
}
