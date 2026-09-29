using MediatR;
using MongoDB.Driver;

namespace integration;

// The Step 1 walk: cursor phases, terminal state and the mix, against real Mongo and Redis.
//
// The fixture's database is assembly-scoped and other suites leave posts in it, so nothing
// here asserts on the whole page. Assertions are scoped to this test's own seeded ids, and the
// phase transitions are read back out of the returned cursor — page content alone cannot tell
// a correct Refresh from one that replayed the corpus, because both serve the newest post
// first.
[Collection(IntegrationCollection.Name)]
public class RelatedPostHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public RelatedPostHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<Account> SeedAccountAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var bcryptService = scope.ServiceProvider.GetRequiredService<IBcryptService>();

        Account account = Account.Create(
            Email.Create($"feed-{Guid.NewGuid():N}@test.invalid").Value!,
            Name.Create($"feed{Guid.NewGuid():N}"[..20]).Value!,
            bcryptService.HashPassword("correct-password"),
            bio: null).Value!;

        await dbContext.GetCollection<Account>(CollectionName.Account).InsertOneAsync(account);

        return account;
    }

    // Each location-aware test gets its own remote origin. The nearby shop list is cached per
    // cell for ten minutes, so two tests sharing a cell would have the second read the first's
    // cached list and never see its own shop.
    private static int _originSeed;

    private static (double Lat, double Lng) UniqueOrigin() =>
        (-40.0 - Interlocked.Increment(ref _originSeed) * 0.5, 170.0);

    private async Task<Shop> SeedShopAsync(double latitude = 21.03, double longitude = 105.85)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        Shop shop = Shop.Create(
            $"shop-{Guid.NewGuid():N}"[..20],
            "1 Test Street",
            latitude,
            longitude,
            $"place-{Guid.NewGuid():N}").Value!;

        await dbContext.GetCollection<Shop>(CollectionName.Shop).InsertOneAsync(shop);

        return shop;
    }

    // Inserted one at a time so CreatedAt strictly increases: the keyset walk orders by
    // (CreatedAt, Id) descending, and a batch sharing one timestamp would make "newest" a
    // tie-break rather than a fact the assertions can rely on.
    private async Task<List<Post>> SeedPostsAsync(Guid accountId, Guid shopId, int count)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var collection = dbContext.GetCollection<Post>(CollectionName.Post);

        var posts = new List<Post>(count);

        for(int i = 0; i < count; i++)
        {
            Post post = Post.Create(accountId, shopId, $"post {i}", Amount.Create(3).Value!).Value!;
            await collection.InsertOneAsync(post);
            posts.Add(post);
        }

        return posts;
    }

    private async Task FollowAsync(Guid viewerId, Account author)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        await dbContext.GetCollection<Account>(CollectionName.Account).UpdateOneAsync(
            Builders<Account>.Filter.Eq(x => x.Id, viewerId),
            Builders<Account>.Update.Push(x => x.Followings, new Following(author.Id, author.Name, null!)));
    }

    private async Task<CursorPageResult<PostFeedItemDto>> QueryAsync(
        Guid viewerId,
        FeedMode mode,
        string? cursor,
        int pageSize,
        double? latitude = null,
        double? longitude = null)
    {
        TestIdentity.Current = viewerId;
        using var scope = _fixture.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(
            new GetRelatedPostQuery(mode, latitude, longitude, new CursorPageRequest(cursor, pageSize)));

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.Message)));

        return result.Value!;
    }

    private static TierCursor Following(string? cursor)
    {
        var decoded = FeedCursor.Decode(cursor);

        Assert.NotNull(decoded);

        return decoded![FeedTier.Following];
    }

    // Live clients hold cursors from the previous feed — a bare base64url mark carrying none
    // of the phase structure. The rollout must not be a 500 on every request until they
    // expire.
    [Fact]
    public async Task LegacyCursor_DegradesToFreshRefresh_RatherThanFailing()
    {
        Account viewer = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        List<Post> posts = await SeedPostsAsync(author.Id, shop.Id, 3);
        await FollowAsync(viewer.Id, author);

        string legacy = CursorCodec.Encode(DateTime.UtcNow, Guid.NewGuid());

        var page = await QueryAsync(viewer.Id, FeedMode.More, legacy, 20);

        var returned = page.Items.Select(x => x.Post.Id).ToHashSet();

        Assert.Contains(posts[^1].Id, returned);
        Assert.NotNull(page.NextCursor);
    }

    // The Flaw 1 / Gap 2 regression. A Phase A spanning more than one page must keep walking
    // on mode=more instead of jumping to Phase B and orphaning everything below the first
    // page, and a walk that reaches the bottom must not then replay itself from the top.
    [Fact]
    public async Task FirstSession_ReturnsEverySeededPostExactlyOnce_AcrossManyPages()
    {
        Account viewer = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        List<Post> posts = await SeedPostsAsync(author.Id, shop.Id, 25);
        await FollowAsync(viewer.Id, author);

        var seeded = posts.Select(x => x.Id).ToHashSet();
        var all = new List<Guid>();
        var blendOnly = new List<Guid>();

        string? cursor = null;
        var mode = FeedMode.Refresh;

        for(int page = 0; page < 40; page++)
        {
            var result = await QueryAsync(viewer.Id, mode, cursor, 10);

            var ids = result.Items.Select(x => x.Post.Id).Where(seeded.Contains).ToList();

            all.AddRange(ids);

            cursor = result.NextCursor;
            mode = FeedMode.More;

            // Once the blend terminates the backstop takes over, and global recent walks the
            // whole collection newest-first — so it legitimately re-serves posts the tier has
            // already shown. That is an accepted cross-page duplicate the client dedupes on
            // append, so the no-duplication assertion below only covers the pages served
            // purely by the blend.
            if(FeedCursor.Decode(cursor)![FeedTier.Global] == TierCursor.Empty)
                blendOnly.AddRange(ids);

            if(seeded.All(all.Contains))
                break;
        }

        // No loss: the walk reached every seeded post.
        Assert.Empty(seeded.Except(all));

        // No duplication: a Phase A that restarted from the top would repeat here.
        Assert.NotEmpty(blendOnly);
        Assert.Equal(blendOnly.Count, blendOnly.Distinct().Count());

        Assert.True(Following(cursor).Exhausted);
    }

    // Gap 2 proper. Page content cannot prove this on its own: a replay walks from the newest
    // too, so its first page looks identical to a correct one. The cursor can — a tier that
    // replayed would have dropped into Phase B with a null BPos.
    [Fact]
    public async Task RefreshAfterExhaustion_WalksOnlyNewPosts_AndNeverEntersPhaseB()
    {
        Account viewer = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        List<Post> original = await SeedPostsAsync(author.Id, shop.Id, 8);
        await FollowAsync(viewer.Id, author);

        var seeded = original.Select(x => x.Id).ToHashSet();
        var returned = new List<Guid>();

        string? cursor = null;
        var mode = FeedMode.Refresh;

        for(int page = 0; page < 20; page++)
        {
            var result = await QueryAsync(viewer.Id, mode, cursor, 5);

            returned.AddRange(result.Items.Select(x => x.Post.Id).Where(seeded.Contains));

            cursor = result.NextCursor;
            mode = FeedMode.More;

            if(seeded.All(returned.Contains))
                break;
        }

        var exhausted = Following(cursor);

        Assert.True(exhausted.Exhausted);
        Assert.NotEqual(FeedPhase.B, exhausted.Phase);

        string? topBefore = exhausted.Top;

        List<Post> fresh = await SeedPostsAsync(author.Id, shop.Id, 3);
        var freshIds = fresh.Select(x => x.Id).ToHashSet();

        var afterRefresh = new List<Guid>();
        mode = FeedMode.Refresh;

        for(int page = 0; page < 20; page++)
        {
            var result = await QueryAsync(viewer.Id, mode, cursor, 5);

            afterRefresh.AddRange(result.Items.Select(x => x.Post.Id).Where(freshIds.Contains));

            cursor = result.NextCursor;
            mode = FeedMode.More;

            if(freshIds.All(afterRefresh.Contains))
                break;
        }

        // Every new post surfaces, each exactly once.
        Assert.Empty(freshIds.Except(afterRefresh));
        Assert.Equal(afterRefresh.Count, afterRefresh.Distinct().Count());

        var after = Following(cursor);

        // Terminal again rather than dropped into Phase B, and the walk was bounded by the
        // previous Top — which is what proves it covered only the new band instead of
        // replaying the eight posts it had already served.
        Assert.True(after.Exhausted);
        Assert.NotEqual(FeedPhase.B, after.Phase);
        Assert.Equal(topBefore, after.Floor);
    }

    // --- Step 2: the nearby tier and the pinned origin ------------------------------------

    [Fact]
    public async Task NearbyTier_ServesPostsFromShopsAroundTheOrigin()
    {
        var (lat, lng) = UniqueOrigin();

        Account viewer = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync(lat, lng);
        List<Post> posts = await SeedPostsAsync(author.Id, shop.Id, 3);

        var page = await QueryAsync(viewer.Id, FeedMode.Refresh, cursor: null, pageSize: 20, lat, lng);

        var returned = page.Items.Select(x => x.Post.Id).ToHashSet();

        Assert.Contains(posts[^1].Id, returned);

        var cursor = FeedCursor.Decode(page.NextCursor);

        Assert.NotNull(cursor);
        Assert.Equal(lat, cursor!.Lat!.Value, 4);
        Assert.Equal(NearbyShopResolver.Cell(lat, lng).Lat, cursor[FeedTier.Nearby].CellLat);
    }

    // The viewer moved. Nearby's marks describe the old neighbourhood's shop set and carrying
    // them over would silently skip a band of the new one — but the tiers that have nothing to
    // do with location must keep their place.
    [Fact]
    public async Task RefreshFromANewCell_RepinsAndResetsNearbyOnly()
    {
        var (lat, lng) = UniqueOrigin();
        var (farLat, farLng) = UniqueOrigin();

        Account viewer = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop near = await SeedShopAsync(lat, lng);
        Shop far = await SeedShopAsync(farLat, farLng);
        await SeedPostsAsync(author.Id, near.Id, 3);
        List<Post> farPosts = await SeedPostsAsync(author.Id, far.Id, 3);
        await FollowAsync(viewer.Id, author);

        var first = await QueryAsync(viewer.Id, FeedMode.Refresh, cursor: null, pageSize: 20, lat, lng);

        var before = FeedCursor.Decode(first.NextCursor)!;

        Assert.NotNull(before[FeedTier.Nearby].Top);

        var moved = await QueryAsync(viewer.Id, FeedMode.Refresh, first.NextCursor, 20, farLat, farLng);

        var after = FeedCursor.Decode(moved.NextCursor)!;

        // Re-pinned to where the viewer actually is.
        Assert.Equal(farLat, after.Lat!.Value, 4);
        Assert.Equal(NearbyShopResolver.Cell(farLat, farLng).Lat, after[FeedTier.Nearby].CellLat);

        // The new area's back catalogue is reachable rather than assumed already delivered.
        Assert.Contains(farPosts[^1].Id, moved.Items.Select(x => x.Post.Id));

        // Following was not collateral damage: its Top survived the nearby reset.
        Assert.Equal(before[FeedTier.Following].Top, after[FeedTier.Following].Top);
    }

    // A viewer walking down the street mid-scroll must not have the nearby walk change corpus
    // underneath its own marks, so More uses the pinned origin and ignores what was sent.
    [Fact]
    public async Task MoreIgnoresSentCoordinates_AndKeepsThePinnedOrigin()
    {
        var (lat, lng) = UniqueOrigin();
        var (otherLat, otherLng) = UniqueOrigin();

        Account viewer = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync(lat, lng);
        await SeedPostsAsync(author.Id, shop.Id, 30);

        var first = await QueryAsync(viewer.Id, FeedMode.Refresh, cursor: null, pageSize: 5, lat, lng);

        var next = await QueryAsync(viewer.Id, FeedMode.More, first.NextCursor, 5, otherLat, otherLng);

        var after = FeedCursor.Decode(next.NextCursor)!;

        Assert.Equal(lat, after.Lat!.Value, 4);
        Assert.Equal(NearbyShopResolver.Cell(lat, lng).Lat, after[FeedTier.Nearby].CellLat);
    }

    // Location permission revoked between sessions. The stale origin must not be reused, or
    // the feed would keep claiming to be local to a place the viewer has left.
    [Fact]
    public async Task RefreshWithoutCoordinates_ClearsThePinAndNearbyMarks()
    {
        var (lat, lng) = UniqueOrigin();

        Account viewer = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync(lat, lng);
        await SeedPostsAsync(author.Id, shop.Id, 3);

        var located = await QueryAsync(viewer.Id, FeedMode.Refresh, cursor: null, pageSize: 20, lat, lng);

        Assert.NotNull(FeedCursor.Decode(located.NextCursor)![FeedTier.Nearby].CellLat);

        var withoutLocation = await QueryAsync(viewer.Id, FeedMode.Refresh, located.NextCursor, 20);

        var after = FeedCursor.Decode(withoutLocation.NextCursor)!;

        Assert.Null(after.Lat);
        Assert.Null(after.Lng);
        Assert.Equal(TierCursor.Empty, after[FeedTier.Nearby]);
    }

    // --- Step 3: reacted exclusion --------------------------------------------------------

    private async Task ReactDirectlyAsync(Guid postId, Account actor)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        await dbContext.GetCollection<Post>(CollectionName.Post).UpdateOneAsync(
            Builders<Post>.Filter.Eq(x => x.Id, postId),
            Builders<Post>.Update.Push(x => x.Reactions, new Reaction(actor.Id, actor.Name, null!)));
    }

    // Reacting is the clearest signal there is that a post has been seen and dealt with, so it
    // does not come round again.
    [Fact]
    public async Task ReactedPost_IsExcludedFromTheFeed()
    {
        Account viewer = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        List<Post> posts = await SeedPostsAsync(author.Id, shop.Id, 3);
        await FollowAsync(viewer.Id, author);

        await ReactDirectlyAsync(posts[^1].Id, viewer);

        var page = await QueryAsync(viewer.Id, FeedMode.Refresh, cursor: null, pageSize: 20);

        var returned = page.Items.Select(x => x.Post.Id).ToHashSet();

        Assert.DoesNotContain(posts[^1].Id, returned);
        Assert.Contains(posts[0].Id, returned);
    }

    // The set is cached, so the reaction has to invalidate it. Without that the post would keep
    // appearing for up to a day — the failure this test exists to catch is a silent one.
    [Fact]
    public async Task ReactingInvalidatesTheCachedSet_SoTheNextRefreshDropsThePost()
    {
        Account viewer = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        List<Post> posts = await SeedPostsAsync(author.Id, shop.Id, 3);
        await FollowAsync(viewer.Id, author);

        // Populates the reacted-set cache with an empty list.
        var before = await QueryAsync(viewer.Id, FeedMode.Refresh, cursor: null, pageSize: 20);

        Assert.Contains(posts[^1].Id, before.Items.Select(x => x.Post.Id));

        using(var scope = _fixture.Services.CreateScope())
        {
            var reactService = scope.ServiceProvider.GetRequiredService<IReactPostService>();
            var result = await reactService.ProcessReactPostAsync(viewer.Id, posts[^1].Id, CancellationToken.None);

            Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.Message)));
        }

        var after = await QueryAsync(viewer.Id, FeedMode.Refresh, cursor: null, pageSize: 20);

        Assert.DoesNotContain(posts[^1].Id, after.Items.Select(x => x.Post.Id));
    }

    // Un-reacting brings the post back. The approved plan accepted the opposite — an
    // append-only list would have kept hiding it until the id aged out — but rebuilding the set
    // from the reactions themselves makes this fall out, and it is the less surprising
    // behaviour: undoing a reaction undoes what the reaction caused.
    [Fact]
    public async Task UnreactingBringsThePostBack()
    {
        Account viewer = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        List<Post> posts = await SeedPostsAsync(author.Id, shop.Id, 3);
        await FollowAsync(viewer.Id, author);

        using(var scope = _fixture.Services.CreateScope())
        {
            var reactService = scope.ServiceProvider.GetRequiredService<IReactPostService>();

            await reactService.ProcessReactPostAsync(viewer.Id, posts[^1].Id, CancellationToken.None);

            var hidden = await QueryAsync(viewer.Id, FeedMode.Refresh, cursor: null, pageSize: 20);
            Assert.DoesNotContain(posts[^1].Id, hidden.Items.Select(x => x.Post.Id));

            var undo = await reactService.ProcessUnreactPostAsync(viewer.Id, posts[^1].Id, CancellationToken.None);
            Assert.True(undo.IsSuccess, string.Join("; ", undo.Errors.Select(e => e.Message)));
        }

        var page = await QueryAsync(viewer.Id, FeedMode.Refresh, cursor: null, pageSize: 20);

        Assert.Contains(posts[^1].Id, page.Items.Select(x => x.Post.Id));
    }

    // The pin is the viewer's own newest post, prepended so posting something is visible
    // immediately. Reacting to your own post must not hide it from its own pin.
    [Fact]
    public async Task OwnPinnedPost_SurvivesReactingToIt()
    {
        Account viewer = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        List<Post> mine = await SeedPostsAsync(viewer.Id, shop.Id, 1);

        await ReactDirectlyAsync(mine[0].Id, viewer);

        var page = await QueryAsync(viewer.Id, FeedMode.Refresh, cursor: null, pageSize: 20);

        Assert.NotEmpty(page.Items);
        Assert.Equal(mine[0].Id, page.Items[0].Post.Id);
    }

    // --- Step 4: the pin bound ------------------------------------------------------------

    private async Task BackdateAsync(Guid postId, TimeSpan age)
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();

        await dbContext.GetCollection<Post>(CollectionName.Post).UpdateOneAsync(
            Builders<Post>.Filter.Eq(x => x.Id, postId),
            Builders<Post>.Update.Set(x => x.CreatedAt, DateTime.UtcNow - age));
    }

    // The pin means "I just posted", not "I posted once". Unbounded, every Refresh for the rest
    // of time would re-pin the same post and the feed would have a stuck header.
    [Fact]
    public async Task OwnPost_OlderThanPinMaxAge_IsNotPinned()
    {
        Account viewer = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        List<Post> mine = await SeedPostsAsync(viewer.Id, shop.Id, 1);

        await BackdateAsync(mine[0].Id, TimeSpan.FromHours(12));

        var page = await QueryAsync(viewer.Id, FeedMode.Refresh, cursor: null, pageSize: 20);

        Assert.DoesNotContain(mine[0].Id, page.Items.Select(x => x.Post.Id));
    }

    [Fact]
    public async Task OwnPost_WithinPinMaxAge_IsPinnedFirst()
    {
        Account viewer = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        List<Post> mine = await SeedPostsAsync(viewer.Id, shop.Id, 1);

        await BackdateAsync(mine[0].Id, TimeSpan.FromHours(2));

        var page = await QueryAsync(viewer.Id, FeedMode.Refresh, cursor: null, pageSize: 20);

        Assert.NotEmpty(page.Items);
        Assert.Equal(mine[0].Id, page.Items[0].Post.Id);
    }

    // The pin sits above the ranking rather than inside it, and ranking reorders only what the
    // allocator already consumed — so neither can move a cursor.
    [Fact]
    public async Task RankingDoesNotDisturbTheCursor()
    {
        Account viewer = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        List<Post> posts = await SeedPostsAsync(author.Id, shop.Id, 12);
        await FollowAsync(viewer.Id, author);

        var seeded = posts.Select(x => x.Id).ToHashSet();
        var returned = new List<Guid>();

        string? cursor = null;
        var mode = FeedMode.Refresh;

        for(int page = 0; page < 20; page++)
        {
            var result = await QueryAsync(viewer.Id, mode, cursor, 4);

            returned.AddRange(result.Items.Select(x => x.Post.Id).Where(seeded.Contains));

            cursor = result.NextCursor;
            mode = FeedMode.More;

            if(seeded.All(returned.Contains))
                break;
        }

        // Every seeded post arrives despite the page order no longer matching the walk order.
        Assert.Empty(seeded.Except(returned));
    }

    // The mix row falls out of which tiers returned sources, and an account with none of them
    // must still be handed something rather than an empty home.
    [Fact]
    public async Task AccountWithNoSources_IsServedByTheBackstop()
    {
        Account viewer = await SeedAccountAsync();
        Account author = await SeedAccountAsync();
        Shop shop = await SeedShopAsync();
        await SeedPostsAsync(author.Id, shop.Id, 3);

        var page = await QueryAsync(viewer.Id, FeedMode.Refresh, cursor: null, pageSize: 5);

        Assert.NotEmpty(page.Items);
    }
}
