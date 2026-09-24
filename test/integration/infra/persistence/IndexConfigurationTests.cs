using MongoDB.Bson;
using MongoDB.Driver;

namespace integration;

[Collection(IntegrationCollection.Name)]
public class IndexConfigurationTests
{
    private readonly IntegrationTestFixture _fixture;

    public IndexConfigurationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<List<BsonDocument>> ShopIndexesAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var mongoClient = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var database = mongoClient.GetDatabase(configuration["MongoDB:DatabaseName"]!);

        using var cursor = await database.GetCollection<Shop>(CollectionName.Shop).Indexes.ListAsync();

        return await cursor.ToListAsync();
    }

    [Fact]
    public async Task Shop_HasAPartialUniqueIndexOnPlaceId()
    {
        var indexes = await ShopIndexesAsync();

        var index = indexes.SingleOrDefault(i => i["key"].AsBsonDocument.Names.SequenceEqual(new[] { "PlaceId" }));

        Assert.NotNull(index);
        Assert.True(index!["unique"].AsBoolean);
        Assert.True(index.Contains("partialFilterExpression"));
    }

    [Fact]
    public async Task Shop_HasAPartialUniqueIndexOnNameAndAddress()
    {
        var indexes = await ShopIndexesAsync();

        var index = indexes.SingleOrDefault(i =>
            i["key"].AsBsonDocument.Names.SequenceEqual(new[] { "Name", "Address" }));

        Assert.NotNull(index);
        Assert.True(index!["unique"].AsBoolean);
        Assert.True(index.Contains("partialFilterExpression"));
    }

    [Fact]
    public async Task Shop_HasA2dsphereIndexOnLocation()
    {
        var indexes = await ShopIndexesAsync();

        var index = indexes.SingleOrDefault(i => i["key"].AsBsonDocument.Contains("Location"));

        Assert.NotNull(index);
        Assert.Equal("2dsphere", index!["key"]["Location"].AsString);
    }
}
