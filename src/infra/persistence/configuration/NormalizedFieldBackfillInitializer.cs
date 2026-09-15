using app;
using domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace infra;

// One-shot migration. Runs as a BackgroundService rather than IHostedService.StartAsync
// so a full scan of shop and post never blocks the app from serving traffic.
//
// Resolves IMongoDbContext from a scope rather than taking it in the constructor: hosted
// services are singletons, and injecting the scoped context directly is a captive
// dependency that ValidateScopes rejects outright — which meant the app would not start
// under Development, where ValidateOnBuild and ValidateScopes are both on.
internal sealed class NormalizedFieldBackfillInitializer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NormalizedFieldBackfillInitializer> _logger;

    public NormalizedFieldBackfillInitializer(
        IServiceScopeFactory scopeFactory,
        ILogger<NormalizedFieldBackfillInitializer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

            await BackfillShopsAsync(dbContext, stoppingToken);
            await BackfillPostsAsync(dbContext, stoppingToken);

            _logger.LogInformation("Normalized field backfill completed");
        }
        catch(Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Normalized field backfill failed");
        }
    }

    private static async Task BackfillShopsAsync(
        IMongoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var collection = dbContext.GetCollection<Shop>(CollectionName.Shop);
        var filter = Builders<Shop>.Filter.Exists(x => x.NameNormalized, false);

        using var cursor = await collection.Find(filter).ToCursorAsync(cancellationToken);
        while(await cursor.MoveNextAsync(cancellationToken))
        {
            var writes = cursor.Current
                .Select(shop => new UpdateOneModel<Shop>(
                    Builders<Shop>.Filter.Eq(x => x.Id, shop.Id),
                    Builders<Shop>.Update
                        .Set(x => x.NameNormalized, TextNormalizer.RemoveDiacriticsAndLower(shop.Name))
                        .Set(x => x.AddressNormalized, TextNormalizer.RemoveDiacriticsAndLower(shop.Address))))
                .ToList();

            if(writes.Count > 0)
                await collection.BulkWriteAsync(writes, cancellationToken: cancellationToken);
        }
    }

    private static async Task BackfillPostsAsync(
        IMongoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var collection = dbContext.GetCollection<Post>(CollectionName.Post);
        var filter = Builders<Post>.Filter.Exists(x => x.CaptionNormalized, false);

        using var cursor = await collection.Find(filter).ToCursorAsync(cancellationToken);
        while(await cursor.MoveNextAsync(cancellationToken))
        {
            var writes = cursor.Current
                .Select(post => new UpdateOneModel<Post>(
                    Builders<Post>.Filter.Eq(x => x.Id, post.Id),
                    Builders<Post>.Update.Set(
                        x => x.CaptionNormalized,
                        post.Caption is null ? null : TextNormalizer.RemoveDiacriticsAndLower(post.Caption))))
                .ToList();

            if(writes.Count > 0)
                await collection.BulkWriteAsync(writes, cancellationToken: cancellationToken);
        }
    }
}
