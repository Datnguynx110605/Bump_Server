using System.Net.Http.Headers;

namespace integration;

// Shared across every area's endpoint tests, not just the auth-tier sweep: minting an
// account + token is the arrange step almost every authenticated test needs, and a fresh
// random IP per anonymous client keeps per-row rate-limit partitions (auth 10/min, email
// 5/min, search 20/min) from bleeding into each other across a MemberData table.
public static class TestAuth
{
    public static string RandomIp() => $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";

    public static HttpClient CreateAnonymousClient(this IntegrationTestFixture fixture)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", RandomIp());

        return client;
    }

    // See AuthTierSweepTests.MintTokenAsync's original note: GetMyAccountHandler and
    // DeleteAccountHandler answer Unauthorized for a token whose accountId has no matching
    // row, so the token must be minted for a real seeded account rather than a bare Guid.
    public static async Task<(HttpClient Client, Account Account, string Token)> CreateAuthenticatedClientAsync(
        this IntegrationTestFixture fixture,
        domain.Role role = domain.Role.User)
    {
        using var scope = fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtService>();

        Account account = Account.Create(
            Email.Create($"test-{Guid.NewGuid():N}@test.invalid").Value!,
            Name.Create($"test{Guid.NewGuid():N}"[..20]).Value!,
            new string('a', 60),
            bio: null).Value!;

        if(role == domain.Role.Admin)
            account.PromoteToAdmin();

        await dbContext.GetCollection<Account>(CollectionName.Account).InsertOneAsync(account);

        string token = jwtService.GenerateAccessToken(account.Id, role);

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, account, token);
    }
}
