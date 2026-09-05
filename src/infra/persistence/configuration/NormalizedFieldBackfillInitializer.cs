using app;
using domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace infra;

// One-shot migration. Runs as a BackgroundService rather than IHostedService.StartAsync
// so a full scan of shop and post never blocks the app from serving traffic.
internal sealed class NormalizedFieldBackfillInitializer : BackgroundService
{
    private readonly IMongoDbContext _dbContext;
    private readonly ILogger<NormalizedFieldBackfillInitializer> _logger;

    public NormalizedFieldBackfillInitializer(
        IMongoDbContext dbContext,
        ILogger<NormalizedFieldBackfillInitializer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await BackfillShopsAsync(stoppingToken);
            await BackfillPostsAsync(stoppingToken);

            _logger.LogInformation("Normalized field backfill completed");
        }
        catch(Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Normalized field backfill failed");
        }
    }

    private async Task BackfillShopsAsync(CancellationToken cancellationToken)
    {
        var collection = _dbContext.GetCollection<Shop>(CollectionName.Shop);
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

    private async Task BackfillPostsAsync(CancellationToken cancellationToken)
    {
        var collection = _dbContext.GetCollection<Post>(CollectionName.Post);
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
