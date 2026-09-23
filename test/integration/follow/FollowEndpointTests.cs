using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MongoDB.Driver;

namespace integration;

// test-strategy.md §5 rows 51-56, following the exact case list §3.5 already spells out
// for FollowService (the one worker the doc says was read in full before this area was
// written): self-follow refused; either account missing -> NotFound; blocked in either
// direction -> refused silently; MatchedCount == 0 -> Conflict; a half-applied edge is
// repaired; both amounts move by exactly 1; trust granted only at >=500 followers AND
// >=30 days old AND no live reports, never revoked by unfollow; and the follow-then-unfollow
// ordering property. FollowHandler/UnfollowHandler check self-follow synchronously (H
// tier, before anything is queued), unlike Reaction's handlers, so that one case is
// asserted through HTTP as well as directly against the worker.
[Collection(IntegrationCollection.Name)]
public class FollowEndpointTests
{
    private readonly IntegrationTestFixture _fixture;

    public FollowEndpointTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path);
        return await client.SendAsync(request);
    }

    private static async Task AssertStatusAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string? bodyFragment = null)
    {
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"expected {expected}, got {response.StatusCode}: {body}");

        if(bodyFragment is not null)
            Assert.Contains(bodyFragment, body);
    }

    private async Task<Account> SeedAccountAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var bcryptService = scope.ServiceProvider.GetRequiredService<IBcryptService>();

        Account account = Account.Create(
            Email.Create($"acct-{Guid.NewGuid():N}@test.invalid").Value!,
            Name.Create($"acct{Guid.NewGuid():N}"[..20]).Value!,
            bcryptService.HashPassword("correct-password"),
            bio: null).Value!;

        await dbContext.GetCollection<Account>(CollectionName.Account).InsertOneAsync(account);

        return account;
    }

    private async Task BlockAsync(Account blocker, Account blocked)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var accountCollection = dbContext.GetCollection<Account>(CollectionName.Account);

        await Task.WhenAll(
            accountCollection.UpdateOneAsync(
                x => x.Id == blocker.Id,
                Builders<Account>.Update.Push(x => x.Blocks, new Block(blocked.Id, blocked.Name, null!))),
            accountCollection.UpdateOneAsync(
                x => x.Id == blocked.Id,
                Builders<Account>.Update.Push(x => x.BlockedBy, blocker.Id)));
    }

    private async Task<Account?> FindAccountAsync(Guid id)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        return await dbContext.GetCollection<Account>(CollectionName.Account).Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    private async Task SetFollowerAmountAndAgeAsync(Guid accountId, int followerAmount, TimeSpan age)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        await dbContext.GetCollection<Account>(CollectionName.Account).UpdateOneAsync(
            x => x.Id == accountId,
            Builders<Account>.Update
                .Set(x => x.FollowerAmount, Amount.Create(followerAmount).Value!)
                .Set(x => x.CreatedAt, DateTime.UtcNow - age));
    }

    private async Task SeedReportAsync(Guid targetAccountId)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        Report report = Report.Create(Guid.NewGuid(), null, null, targetAccountId, "reason").Value!;
        await dbContext.GetCollection<Report>(CollectionName.Report).InsertOneAsync(report);
    }

    private HttpClient AuthenticatedClientFor(Account account)
    {
        using var scope = _fixture.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtService>();
        string token = jwtService.GenerateAccessToken(account.Id, domain.Role.User);

        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    // ---------------------------------------------------------------- #51/#52 POST,DELETE /Follow/{id} — E tier

    [Fact]
    public async Task Follow_Self_Returns400()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Post, $"api/v1/Follow/{account.Id}");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Cannot follow yourself");
    }

    [Fact]
    public async Task Unfollow_Self_Returns400()
    {
        Account account = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(account);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Follow/{account.Id}");

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, "Cannot unfollow yourself");
    }

    [Fact]
    public async Task Follow_Valid_Returns202_AndPublishesWithIsUndoFalse()
    {
        Account follower = await SeedAccountAsync();
        Account following = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(follower);

        var response = await SendAsync(client, HttpMethod.Post, $"api/v1/Follow/{following.Id}");

        await AssertStatusAsync(response, HttpStatusCode.Accepted);
        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.Follow &&
            m.Message is FollowMessage fm && fm.FollowerId == follower.Id && fm.FollowingId == following.Id && !fm.IsUndo);
    }

    [Fact]
    public async Task Unfollow_Valid_Returns202_AndPublishesWithIsUndoTrue()
    {
        Account follower = await SeedAccountAsync();
        Account following = await SeedAccountAsync();
        using var client = AuthenticatedClientFor(follower);

        var response = await SendAsync(client, HttpMethod.Delete, $"api/v1/Follow/{following.Id}");

        await AssertStatusAsync(response, HttpStatusCode.Accepted);
        Assert.Contains(_fixture.PublishedMessages.Messages, m =>
            m.QueueName == QueueName.Follow &&
            m.Message is FollowMessage fm && fm.FollowerId == follower.Id && fm.FollowingId == following.Id && fm.IsUndo);
    }

    // ---------------------------------------------------------------- FollowService — W tier

    [Fact]
    public async Task ProcessFollow_Self_Fails()
    {
        Account account = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFollowService>();

        var result = await service.ProcessFollowAsync(account.Id, account.Id, CancellationToken.None);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task ProcessFollow_FollowerNotFound_Fails()
    {
        Account following = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFollowService>();

        var result = await service.ProcessFollowAsync(Guid.NewGuid(), following.Id, CancellationToken.None);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task ProcessFollow_FollowingNotFound_Fails()
    {
        Account follower = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFollowService>();

        var result = await service.ProcessFollowAsync(follower.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task ProcessFollow_Blocked_FailsSilently_NoEdgeWritten()
    {
        Account follower = await SeedAccountAsync();
        Account following = await SeedAccountAsync();
        await BlockAsync(following, follower);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFollowService>();

        var result = await service.ProcessFollowAsync(follower.Id, following.Id, CancellationToken.None);

        Assert.True(result.IsFailed);
        Account? storedFollower = await FindAccountAsync(follower.Id);
        Assert.Empty(storedFollower!.Followings);
    }

    // §3.5 says "blocked in either direction": the case above trips the BlockedBy half of
    // BlockGuard's Or filter (the target blocked the actor); this one trips the Blocks half
    // (the actor blocked the target and then tries to follow them anyway) — a different
    // branch of the same filter.
    [Fact]
    public async Task ProcessFollow_BlockedTheOtherDirection_FailsSilently_NoEdgeWritten()
    {
        Account follower = await SeedAccountAsync();
        Account following = await SeedAccountAsync();
        await BlockAsync(follower, following);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFollowService>();

        var result = await service.ProcessFollowAsync(follower.Id, following.Id, CancellationToken.None);

        Assert.True(result.IsFailed);
        Account? storedFollower = await FindAccountAsync(follower.Id);
        Assert.Empty(storedFollower!.Followings);
    }

    [Fact]
    public async Task ProcessFollow_Valid_WritesBothSides_IncrementsBothCountersByOne()
    {
        Account follower = await SeedAccountAsync();
        Account following = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFollowService>();

        var result = await service.ProcessFollowAsync(follower.Id, following.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Account? storedFollower = await FindAccountAsync(follower.Id);
        Account? storedFollowing = await FindAccountAsync(following.Id);

        Assert.Single(storedFollower!.Followings);
        Assert.Equal(1, storedFollower.FollowingAmount.Value);
        Assert.Single(storedFollowing!.Followers);
        Assert.Equal(1, storedFollowing.FollowerAmount.Value);
    }

    [Fact]
    public async Task ProcessFollow_DoubleFollow_ReturnsConflict_NoDoubleIncrement()
    {
        Account follower = await SeedAccountAsync();
        Account following = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFollowService>();
        await service.ProcessFollowAsync(follower.Id, following.Id, CancellationToken.None);

        var second = await service.ProcessFollowAsync(follower.Id, following.Id, CancellationToken.None);

        Assert.True(second.IsFailed);
        Account? storedFollowing = await FindAccountAsync(following.Id);
        Assert.Equal(1, storedFollowing!.FollowerAmount.Value);
    }

    // Pins §3.5's "a pre-broken half-applied edge is repaired rather than throwing": the
    // follower's own Followings array is seeded directly (as if a previous attempt crashed
    // after the first bulk-write leg), and ProcessFollowAsync is expected to notice the
    // following side is still missing and add only that side. The end state this asserts
    // is deliberately identical to ProcessFollow_Valid_...'s — that sameness is the point,
    // since a caller can't tell a repaired edge from a normally-applied one and neither
    // should this test. What makes this a repair rather than a normal follow is the seed,
    // not a different assertion: Followings already had the edge before the call ran, so a
    // second push (rather than the filter correctly excluding that side) would have shown
    // up as Followings.Count == 2, which Assert.Single below still catches.
    [Fact]
    public async Task ProcessFollow_HalfAppliedEdge_IsRepaired()
    {
        Account follower = await SeedAccountAsync();
        Account following = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        await dbContext.GetCollection<Account>(CollectionName.Account).UpdateOneAsync(
            x => x.Id == follower.Id,
            Builders<Account>.Update
                .Push(x => x.Followings, new Following(following.Id, following.Name, null!))
                .Inc(x => x.FollowingAmount.Value, 1));

        var service = scope.ServiceProvider.GetRequiredService<IFollowService>();
        var result = await service.ProcessFollowAsync(follower.Id, following.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Account? storedFollower = await FindAccountAsync(follower.Id);
        Account? storedFollowing = await FindAccountAsync(following.Id);

        Assert.Single(storedFollower!.Followings);
        Assert.Equal(1, storedFollower.FollowingAmount.Value);
        Assert.Single(storedFollowing!.Followers);
        Assert.Equal(1, storedFollowing.FollowerAmount.Value);
    }

    [Fact]
    public async Task ProcessUnfollow_NotFollowing_Fails()
    {
        Account follower = await SeedAccountAsync();
        Account following = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFollowService>();

        var result = await service.ProcessUnfollowAsync(follower.Id, following.Id, CancellationToken.None);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task ProcessUnfollow_Valid_RemovesBothSides_DecrementsBothCounters()
    {
        Account follower = await SeedAccountAsync();
        Account following = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFollowService>();
        await service.ProcessFollowAsync(follower.Id, following.Id, CancellationToken.None);

        var result = await service.ProcessUnfollowAsync(follower.Id, following.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Account? storedFollower = await FindAccountAsync(follower.Id);
        Account? storedFollowing = await FindAccountAsync(following.Id);
        Assert.Empty(storedFollower!.Followings);
        Assert.Equal(0, storedFollower.FollowingAmount.Value);
        Assert.Empty(storedFollowing!.Followers);
        Assert.Equal(0, storedFollowing.FollowerAmount.Value);
    }

    // Pins the ordering property §3.5 calls out explicitly: the two commands share one
    // queue precisely so a rapid follow/unfollow pair applies in order.
    [Fact]
    public async Task FollowThenUnfollow_BackToBack_LeavesNoEdgeAndCountersAtZero()
    {
        Account follower = await SeedAccountAsync();
        Account following = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFollowService>();
        await service.ProcessFollowAsync(follower.Id, following.Id, CancellationToken.None);
        var unfollowResult = await service.ProcessUnfollowAsync(follower.Id, following.Id, CancellationToken.None);

        Assert.True(unfollowResult.IsSuccess);
        Account? storedFollower = await FindAccountAsync(follower.Id);
        Account? storedFollowing = await FindAccountAsync(following.Id);
        Assert.Empty(storedFollower!.Followings);
        Assert.Equal(0, storedFollower.FollowingAmount.Value);
        Assert.Empty(storedFollowing!.Followers);
        Assert.Equal(0, storedFollowing.FollowerAmount.Value);
    }

    // ---------------------------------------------------------------- Trust grant — W tier

    [Fact]
    public async Task ProcessFollow_AllThreeConditionsMet_GrantsTrust()
    {
        Account follower = await SeedAccountAsync();
        Account following = await SeedAccountAsync();
        await SetFollowerAmountAndAgeAsync(following.Id, 499, TimeSpan.FromDays(31));

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFollowService>();

        var result = await service.ProcessFollowAsync(follower.Id, following.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Account? stored = await FindAccountAsync(following.Id);
        Assert.True(stored!.IsTrusted);
    }

    [Fact]
    public async Task ProcessFollow_UnderFollowerThreshold_DoesNotGrantTrust()
    {
        Account follower = await SeedAccountAsync();
        Account following = await SeedAccountAsync();
        await SetFollowerAmountAndAgeAsync(following.Id, 100, TimeSpan.FromDays(31));

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFollowService>();

        await service.ProcessFollowAsync(follower.Id, following.Id, CancellationToken.None);

        Account? stored = await FindAccountAsync(following.Id);
        Assert.False(stored!.IsTrusted);
    }

    [Fact]
    public async Task ProcessFollow_AccountTooYoung_DoesNotGrantTrust()
    {
        Account follower = await SeedAccountAsync();
        Account following = await SeedAccountAsync();
        await SetFollowerAmountAndAgeAsync(following.Id, 499, TimeSpan.FromDays(1));

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFollowService>();

        await service.ProcessFollowAsync(follower.Id, following.Id, CancellationToken.None);

        Account? stored = await FindAccountAsync(following.Id);
        Assert.False(stored!.IsTrusted);
    }

    [Fact]
    public async Task ProcessFollow_HasLiveReport_DoesNotGrantTrust()
    {
        Account follower = await SeedAccountAsync();
        Account following = await SeedAccountAsync();
        await SetFollowerAmountAndAgeAsync(following.Id, 499, TimeSpan.FromDays(31));
        await SeedReportAsync(following.Id);

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFollowService>();

        await service.ProcessFollowAsync(follower.Id, following.Id, CancellationToken.None);

        Account? stored = await FindAccountAsync(following.Id);
        Assert.False(stored!.IsTrusted);
    }

    [Fact]
    public async Task ProcessUnfollow_AfterTrustGranted_TrustIsNotRevoked()
    {
        Account follower = await SeedAccountAsync();
        Account following = await SeedAccountAsync();
        await SetFollowerAmountAndAgeAsync(following.Id, 499, TimeSpan.FromDays(31));

        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFollowService>();
        await service.ProcessFollowAsync(follower.Id, following.Id, CancellationToken.None);

        Account? trusted = await FindAccountAsync(following.Id);
        Assert.True(trusted!.IsTrusted);

        await service.ProcessUnfollowAsync(follower.Id, following.Id, CancellationToken.None);

        Account? afterUnfollow = await FindAccountAsync(following.Id);
        Assert.True(afterUnfollow!.IsTrusted);
    }

    // ---------------------------------------------------------------- #53 GET /Follow/follower/{accountId}

    [Fact]
    public async Task GetFollowerByAccount_BlockedFollower_FilteredButPageStillFull()
    {
        Account viewer = await SeedAccountAsync();
        Account blockedFollower = await SeedAccountAsync();
        Account target = await SeedAccountAsync();
        await BlockAsync(viewer, blockedFollower);

        using var scope = _fixture.Services.CreateScope();
        var followService = scope.ServiceProvider.GetRequiredService<IFollowService>();
        await followService.ProcessFollowAsync(blockedFollower.Id, target.Id, CancellationToken.None);

        var visibleIds = new List<Guid>();
        for(int i = 0; i < 20; i++)
        {
            Account follower = await SeedAccountAsync();
            await followService.ProcessFollowAsync(follower.Id, target.Id, CancellationToken.None);
            visibleIds.Add(follower.Id);
        }

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Follow/follower/{target.Id}?pageSize=20");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("accountId").GetGuid()).ToList();
        Assert.Equal(20, ids.Count);
        Assert.All(visibleIds, id => Assert.Contains(id, ids));
    }

    // ---------------------------------------------------------------- #54 GET /Follow/following/{accountId}

    [Fact]
    public async Task GetFollowingByAccount_BlockedFollowing_IsFiltered()
    {
        Account viewer = await SeedAccountAsync();
        Account blockedTarget = await SeedAccountAsync();
        Account visibleTarget = await SeedAccountAsync();
        Account owner = await SeedAccountAsync();
        await BlockAsync(viewer, blockedTarget);

        using var scope = _fixture.Services.CreateScope();
        var followService = scope.ServiceProvider.GetRequiredService<IFollowService>();
        await followService.ProcessFollowAsync(owner.Id, blockedTarget.Id, CancellationToken.None);
        await followService.ProcessFollowAsync(owner.Id, visibleTarget.Id, CancellationToken.None);

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, $"api/v1/Follow/following/{owner.Id}");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("accountId").GetGuid()).ToList();
        Assert.Contains(visibleTarget.Id, ids);
        Assert.DoesNotContain(blockedTarget.Id, ids);
    }

    // ---------------------------------------------------------------- #55/#56 GET /Follow/me/follower, /me/following

    [Fact]
    public async Task GetMyFollower_ReturnsOwnFollowersOnly()
    {
        Account viewer = await SeedAccountAsync();
        Account other = await SeedAccountAsync();
        Account follower = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var followService = scope.ServiceProvider.GetRequiredService<IFollowService>();
        await followService.ProcessFollowAsync(follower.Id, viewer.Id, CancellationToken.None);
        await followService.ProcessFollowAsync(follower.Id, other.Id, CancellationToken.None);

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Follow/me/follower");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("accountId").GetGuid()).ToList();
        Assert.Equal([follower.Id], ids);
    }

    [Fact]
    public async Task GetMyFollowing_ReturnsOwnFollowingsOnly()
    {
        Account viewer = await SeedAccountAsync();
        Account other = await SeedAccountAsync();
        Account followed = await SeedAccountAsync();

        using var scope = _fixture.Services.CreateScope();
        var followService = scope.ServiceProvider.GetRequiredService<IFollowService>();
        await followService.ProcessFollowAsync(viewer.Id, followed.Id, CancellationToken.None);
        await followService.ProcessFollowAsync(other.Id, followed.Id, CancellationToken.None);

        using var client = AuthenticatedClientFor(viewer);
        var response = await SendAsync(client, HttpMethod.Get, "api/v1/Follow/me/following");
        await AssertStatusAsync(response, HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("accountId").GetGuid()).ToList();
        Assert.Equal([followed.Id], ids);
    }
}
