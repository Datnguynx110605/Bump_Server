using StackExchange.Redis;

namespace integration;

[Collection(IntegrationCollection.Name)]
public class RedisServiceTests
{
    private readonly IntegrationTestFixture _fixture;

    public RedisServiceTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static string Key([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"redis-service-test:{caller}:{Guid.NewGuid():N}";

    [Fact]
    public async Task SetGetRemove_RoundTripsAValueObjectThroughTheDomainJsonConverters()
    {
        using var scope = _fixture.Services.CreateScope();
        var redis = scope.ServiceProvider.GetRequiredService<IRedisService>();
        string key = Key();
        var email = Email.Create("redis-roundtrip@test.invalid").Value!;

        await redis.SetAsync(key, email, ttl: null, CancellationToken.None);
        var read = await redis.GetAsync<Email>(key, CancellationToken.None);

        Assert.Equal(email.Value, read!.Value);

        await redis.RemoveAsync(key, CancellationToken.None);
        var afterRemove = await redis.GetAsync<Email>(key, CancellationToken.None);

        Assert.Null(afterRemove);
    }

    [Fact]
    public async Task SetAsync_AppliesTheGivenTTL()
    {
        using var scope = _fixture.Services.CreateScope();
        var redis = scope.ServiceProvider.GetRequiredService<IRedisService>();
        var connectionMultiplexer = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        string key = Key();

        await redis.SetAsync(key, "value", TimeSpan.FromMinutes(5), CancellationToken.None);
        var ttl = await connectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(key);

        Assert.NotNull(ttl);
        Assert.True(ttl!.Value > TimeSpan.Zero && ttl.Value <= TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task IncrementAsync_CreatesTheCounterAndAppliesTheGivenTTL()
    {
        using var scope = _fixture.Services.CreateScope();
        var redis = scope.ServiceProvider.GetRequiredService<IRedisService>();
        var connectionMultiplexer = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        string key = Key();

        var first = await redis.IncrementAsync(key, TimeSpan.FromMinutes(5), CancellationToken.None);
        var second = await redis.IncrementAsync(key, TimeSpan.FromMinutes(5), CancellationToken.None);
        var ttl = await connectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(key);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.NotNull(ttl);
    }

    [Fact]
    public async Task GetCounterAsync_MissingKey_ReturnsZero()
    {
        using var scope = _fixture.Services.CreateScope();
        var redis = scope.ServiceProvider.GetRequiredService<IRedisService>();

        var counter = await redis.GetCounterAsync(Key(), CancellationToken.None);

        Assert.Equal(0, counter);
    }
}
