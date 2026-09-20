using app;
using domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace infra;

// Recurring hard-delete sweep for rows that stayed soft-deleted past the retention
// window. Runs as a BackgroundService rather than IHostedService.StartAsync, and the
// first pass is held back by StartupDelay, so a deploy never pays for the sweep while
// the app is still coming up. Deletes are batched with a pause between batches so a
// large backlog never lands as one long write burst.
//
// R2 objects are removed at soft-delete time by the delete handlers and the queue
// cascades, so the sweep itself is pure Mongo work. That holds only while those cascades
// actually run: a cascade message that dead-letters leaves objects in the bucket that this
// sweep then makes unreachable by hard-deleting the row that named them. Nothing reconciles
// that today.
//
// Only one replica sweeps per interval — see the claim in SweepAsync.
internal sealed class SoftDeletePurgeHost : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan BatchDelay = TimeSpan.FromMilliseconds(200);
    private const int BatchSize = 500;
    private const string SweepClaimKey = "purge:sweep-claim";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SoftDeletePurgeHost> _logger;

    public SoftDeletePurgeHost(
        IServiceScopeFactory scopeFactory,
        ILogger<SoftDeletePurgeHost> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, stoppingToken);

        using var timer = new PeriodicTimer(SweepInterval);

        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch(Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Soft-delete purge failed");
            }
        }
        while(await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();

        // One replica sweeps per interval. Every instance runs this host — the worker engine
        // is co-located with the request surface, so scaling request capacity scales the
        // number of sweepers too — and they all wake on the same schedule from the same
        // deploy. Without this they would walk the same batches concurrently, each deleting
        // rows the others had just listed, turning a paced background job into a burst that
        // scales with replica count.
        //
        // The counter carries the sweep interval as its TTL, so it expires in time for the
        // next one and the winner rotates naturally as replicas restart.
        long claim = await redisService.IncrementAsync(
            SweepClaimKey,
            SweepInterval,
            cancellationToken);

        if(claim != 1)
            return;

        var accountCollection = dbContext.GetCollection<Account>(CollectionName.Account);
        var postCollection = dbContext.GetCollection<Post>(CollectionName.Post);
        var commentCollection = dbContext.GetCollection<Comment>(CollectionName.Comment);
        var shopCollection = dbContext.GetCollection<Shop>(CollectionName.Shop);
        var refreshTokenCollection = dbContext.GetCollection<RefreshToken>(CollectionName.RefreshToken);
        var deviceTokenCollection = dbContext.GetCollection<DeviceToken>(CollectionName.DeviceToken);

        // Rows soft-deleted by a cascade before DeletedAt was stamped there have no clock
        // to expire on. Give them one: they start their retention window at the first sweep
        // that sees them rather than being purged immediately or never.
        await StampDeletedAtAsync(accountCollection, cancellationToken);
        await StampDeletedAtAsync(postCollection, cancellationToken);
        await StampDeletedAtAsync(commentCollection, cancellationToken);
        await StampDeletedAtAsync(shopCollection, cancellationToken);

        var cutoff = DateTime.UtcNow - Retention;

        var comments = await PurgeAsync(commentCollection, ExpiredFilter<Comment>(cutoff), cancellationToken);
        var posts = await PurgeAsync(postCollection, ExpiredFilter<Post>(cutoff), cancellationToken);
        var accounts = await PurgeAsync(accountCollection, ExpiredFilter<Account>(cutoff), cancellationToken);
        var shops = await PurgeAsync(shopCollection, ExpiredFilter<Shop>(cutoff), cancellationToken);

        // Revoked tokens are unusable, so they go on sight. The ExpiryDate TTL index would
        // reach them within 7 days anyway; this only shortens the window.
        var refreshTokens = await PurgeAsync(
            refreshTokenCollection,
            Builders<RefreshToken>.Filter.Eq(x => x.IsRevoked, true),
            cancellationToken);

        var deviceTokens = await PurgeAsync(
            deviceTokenCollection,
            ExpiredFilter<DeviceToken>(cutoff),
            cancellationToken);

        if(accounts + posts + comments + shops + refreshTokens + deviceTokens > 0)
            _logger.LogInformation(
                "Soft-delete purge removed {Accounts} accounts, {Posts} posts, {Comments} comments, {Shops} shops, {RefreshTokens} refresh tokens, {DeviceTokens} device tokens",
                accounts,
                posts,
                comments,
                shops,
                refreshTokens,
                deviceTokens);
    }

    private static FilterDefinition<T> ExpiredFilter<T>(DateTime cutoff) where T : BaseEntity
    {
        return Builders<T>.Filter.And(
            Builders<T>.Filter.Eq(x => x.IsDeleted, true),
            Builders<T>.Filter.Lt(x => x.DeletedAt, cutoff));
    }

    private static Task StampDeletedAtAsync<T>(
        IMongoCollection<T> collection,
        CancellationToken cancellationToken) where T : BaseEntity
    {
        var filter = Builders<T>.Filter.And(
            Builders<T>.Filter.Eq(x => x.IsDeleted, true),
            Builders<T>.Filter.Eq(x => x.DeletedAt, null));

        var update = Builders<T>.Update.Set(x => x.DeletedAt, DateTime.UtcNow);

        return collection.UpdateManyAsync(
            filter,
            update,
            cancellationToken : cancellationToken);
    }

    private static async Task<long> PurgeAsync<T>(
        IMongoCollection<T> collection,
        FilterDefinition<T> filter,
        CancellationToken cancellationToken) where T : BaseEntity
    {
        long deleted = 0;

        while(true)
        {
            var ids = await collection
                .Find(filter)
                .Limit(BatchSize)
                .Project(x => x.Id)
                .ToListAsync(cancellationToken);

            if(ids.Count == 0)
                return deleted;

            // The filter is repeated in the delete, not just the ids: a row recovered
            // between the find and the delete must survive, and this is a hard delete.
            var result = await collection.DeleteManyAsync(
                Builders<T>.Filter.And(filter, Builders<T>.Filter.In(x => x.Id, ids)),
                cancellationToken);

            deleted += result.DeletedCount;

            // A full batch that deleted nothing means the rows stopped matching; stop
            // rather than re-reading the same page forever.
            if(ids.Count < BatchSize || result.DeletedCount == 0)
                return deleted;

            await Task.Delay(BatchDelay, cancellationToken);
        }
    }
}
