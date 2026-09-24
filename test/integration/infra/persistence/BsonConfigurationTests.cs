using MongoDB.Driver;

namespace integration;

[Collection(IntegrationCollection.Name)]
public class BsonConfigurationTests
{
    private readonly IntegrationTestFixture _fixture;

    public BsonConfigurationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Account_WithPopulatedBackingFields_RoundTripsWithThemIntact_AndValueObjectsThroughTheirPrivateConstructors()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var collection = dbContext.GetCollection<Account>(CollectionName.Account);

        var account = Account.Create(
            Email.Create($"bson-{Guid.NewGuid():N}@test.invalid").Value!,
            Name.Create($"bson{Guid.NewGuid():N}"[..15]).Value!,
            new string('a', 60),
            bio: null).Value!;
        await collection.InsertOneAsync(account);

        var avatar = new Avatar("http://cdn.test.invalid/avatar.png");
        var followerId = Guid.NewGuid();
        var follower = new Follower(followerId, Name.Create("followername").Value!, avatar);
        var blockedId = Guid.NewGuid();
        var block = new Block(blockedId, Name.Create("blockedname").Value!, avatar);
        var blockedByAccountId = Guid.NewGuid();

        await collection.UpdateOneAsync(
            Builders<Account>.Filter.Eq(x => x.Id, account.Id),
            Builders<Account>.Update
                .Push(x => x.Avatars, avatar)
                .Push(x => x.Followers, follower)
                .Push(x => x.Blocks, block)
                .Push(x => x.BlockedBy, blockedByAccountId));

        var reread = await collection.Find(x => x.Id == account.Id).FirstOrDefaultAsync();

        Assert.Single(reread.Avatars);
        Assert.Equal(avatar.Url, reread.Avatars.Single().Url);
        Assert.Single(reread.Followers);
        Assert.Equal(followerId, reread.Followers.Single().AccountId);
        Assert.Single(reread.Blocks);
        Assert.Equal(blockedId, reread.Blocks.Single().AccountId);
        Assert.Single(reread.BlockedBy);
        Assert.Equal(blockedByAccountId, reread.BlockedBy.Single());

        // Deserialized through Email/Name/Amount's own private constructors, not a
        // parameterless one BsonConfiguration never registers for them.
        Assert.Equal(account.Email.Value, reread.Email.Value);
        Assert.Equal(account.Name.Value, reread.Name.Value);
        Assert.Equal(0, reread.FollowerAmount.Value);
    }
}
