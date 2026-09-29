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

    // Asserted absent, not merely unlisted. Apple can answer with two distinct places that
    // format to the same name and address — two kiosks in one mall, two units at one street
    // number — and a unique index over that text would reject the second of them as a
    // duplicate, turning a valid shop into a 409.
    [Fact]
    public async Task Shop_HasNoUniqueIndexOverNameOrAddress()
    {
        var indexes = await ShopIndexesAsync();

        Assert.DoesNotContain(indexes, i =>
            i.GetValue("unique", false).ToBoolean()
            && i["key"].AsBsonDocument.Names.Any(name =>
                name is "Name" or "Address" or "NameNormalized" or "AddressNormalized"));
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
