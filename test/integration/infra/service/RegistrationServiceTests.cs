using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace integration;

[Collection(IntegrationCollection.Name)]
public class RegistrationServiceTests
{
    private readonly IntegrationTestFixture _fixture;

    public RegistrationServiceTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ExtractEmailAsync_DoesNotConsumeTheToken_ItSurvivesARepeatRead()
    {
        using var scope = _fixture.Services.CreateScope();
        var registration = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
        string email = $"reg-test-{Guid.NewGuid():N}@test.invalid";
        string token = await registration.GenerateTokenAsync(email, CancellationToken.None);

        var first = await registration.ExtractEmailAsync(token, CancellationToken.None);
        var second = await registration.ExtractEmailAsync(token, CancellationToken.None);

        Assert.Equal(email, first);
        Assert.Equal(email, second);
    }

    [Fact]
    public async Task ExtractEmailAsync_TamperedToken_ReturnsNullRatherThanThrowing()
    {
        using var scope = _fixture.Services.CreateScope();
        var registration = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
        string email = $"reg-test-{Guid.NewGuid():N}@test.invalid";
        string token = await registration.GenerateTokenAsync(email, CancellationToken.None);

        // RegistrationService protects the EMAIL and keys Redis by the token, so tampering
        // has to replace the stored payload with something that is valid base64url (or
        // GetAsync<string> never even returns a value to unprotect) but fails
        // IDataProtector.Unprotect's authentication check — random bytes of protected-payload
        // length do exactly that, exercising the CryptographicException path directly rather
        // than a decode failure earlier in the pipeline.
        var redis = scope.ServiceProvider.GetRequiredService<IRedisService>();
        string garbage = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        await redis.SetAsync($"registration:{token}", garbage, TimeSpan.FromMinutes(10), CancellationToken.None);

        var result = await registration.ExtractEmailAsync(token, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConsumeTokenAsync_RemovesTheToken()
    {
        using var scope = _fixture.Services.CreateScope();
        var registration = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
        string email = $"reg-test-{Guid.NewGuid():N}@test.invalid";
        string token = await registration.GenerateTokenAsync(email, CancellationToken.None);

        await registration.ConsumeTokenAsync(token, CancellationToken.None);
        var afterConsume = await registration.ExtractEmailAsync(token, CancellationToken.None);

        Assert.Null(afterConsume);
    }
}
