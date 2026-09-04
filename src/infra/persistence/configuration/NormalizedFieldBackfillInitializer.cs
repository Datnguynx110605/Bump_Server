using app;
using domain;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;

namespace infra;

internal sealed class NormalizedFieldBackfillInitializer : IHostedService
{
    private readonly IMongoDbContext _dbContext;

    public NormalizedFieldBackfillInitializer(IMongoDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await BackfillShopsAsync(cancellationToken);
        await BackfillPostsAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
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
